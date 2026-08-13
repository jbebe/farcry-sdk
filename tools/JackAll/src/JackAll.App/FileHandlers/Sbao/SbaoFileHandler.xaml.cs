using JackAll.App.Audio;
using JackAll.Tools.Sbao;
using Microsoft.Win32;
using System.IO;
using System.Windows.Controls;
using System.Windows;

namespace JackAll.App.FileHandlers.Sbao;

/// <summary>
/// The file handler for Ogg-backed .sbao audio (music, dialogue). Splits the file into its
/// engine header and embedded Ogg Vorbis payload on load, offers export as the original Ogg or as
/// mp3, imports any ffmpeg-readable audio (transcoded to Far Cry 2's required 48 kHz stereo Ogg
/// Vorbis and staged into the workspace), and previews it via <see cref="Audio.AudioPreviewPanel"/>.
/// </summary>
public partial class SbaoFileHandler : UserControl
{
    private readonly string _fileName;
    private readonly Action<byte[]> _replaceContent;
    private byte[]? _header;
    private byte[]? _ogg;
    private string? _tempOggPath;
    private string? _tempWavPath;

    public SbaoFileHandler(string fileName, byte[] content, Action<byte[]> replaceContent)
    {
        InitializeComponent();
        _fileName = fileName;
        _replaceContent = replaceContent;

        // Release the temp .wav before deleting it - the panel resets itself on Unloaded too, but
        // child/parent Unloaded order isn't guaranteed.
        Unloaded += (_, _) =>
        {
            Preview.Reset();
            DeleteTempFiles();
        };

        Load(content);
    }

    private void Load(byte[] content)
    {
        try
        {
            (byte[] header, byte[] ogg) = SbaoAudio.Split(content);
            _header = header;
            _ogg = ogg;

            var vorbis = SbaoAudio.TryReadVorbisId(ogg);
            bool expectedFormat = vorbis is
                { SampleRate: FfmpegAudio.RequiredSampleRate, Channels: FfmpegAudio.RequiredChannels };

            StatusText.Text =
                $"{_fileName}\n\n" +
                $"Header: {header.Length:N0} bytes\n" +
                $"Ogg payload: {ogg.Length:N0} bytes\n" +
                (vorbis is { } v
                    ? $"Vorbis: {v.SampleRate} Hz, {v.Channels} ch" +
                      (expectedFormat ? "" : $"  <- differs from Far Cry 2's required " +
                          $"{FfmpegAudio.RequiredSampleRate} Hz / {FfmpegAudio.RequiredChannels} ch")
                    : "Vorbis identification header not recognized") +
                "\n\nReady to export.";

            ExportButton.IsEnabled = true;
            _ = PreparePreviewAsync(ogg);
        }
        catch (Exception ex)
        {
            _header = null;
            _ogg = null;
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
            ExportButton.IsEnabled = false;
            Preview.Reset();
        }
    }

    private async Task PreparePreviewAsync(byte[] ogg)
    {
        Preview.Reset();
        DeleteTempFiles();

        try
        {
            _tempOggPath = Path.Combine(Path.GetTempPath(), $"jackall_sbao_{Guid.NewGuid():N}.ogg");
            _tempWavPath = Path.ChangeExtension(_tempOggPath, ".wav");
            await File.WriteAllBytesAsync(_tempOggPath, ogg);
            await FfmpegAudio.TranscodeToWavAsync(_tempOggPath, _tempWavPath);

            Preview.Open(_tempWavPath);
        }
        catch (Exception ex)
        {
            StatusText.Text += $"\n\nNo audio preview available: {ex.Message}";
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_ogg is null)
        {
            return;
        }

        bool asMp3 = ExportFormatCombo.SelectedIndex == 1;
        var dialog = new SaveFileDialog
        {
            Title = "Export audio",
            FileName = Path.GetFileNameWithoutExtension(_fileName) + (asMp3 ? ".mp3" : ".ogg"),
            Filter = asMp3 ? "MP3 file|*.mp3" : "Ogg Vorbis file|*.ogg",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        ExportButton.IsEnabled = false;
        try
        {
            if (asMp3)
            {
                string tempOgg = Path.Combine(Path.GetTempPath(), $"jackall_sbao_export_{Guid.NewGuid():N}.ogg");
                try
                {
                    await File.WriteAllBytesAsync(tempOgg, _ogg);
                    await FfmpegAudio.TranscodeToMp3Async(tempOgg, dialog.FileName);
                }
                finally
                {
                    TryDelete(tempOgg);
                }
            }
            else
            {
                await File.WriteAllBytesAsync(dialog.FileName, _ogg);
            }

            StatusText.Text += $"\n\nExported to:\n{dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't export: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_header is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Import replacement audio - any format ffmpeg supports",
            Filter = "Audio files|*.ogg;*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.opus;*.aiff|All files|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        ImportButton.IsEnabled = false;
        string tempOgg = Path.Combine(Path.GetTempPath(), $"jackall_sbao_import_{Guid.NewGuid():N}.ogg");
        try
        {
            StatusText.Text += "\n\nTranscoding to 48 kHz stereo Ogg Vorbis…";
            await FfmpegAudio.TranscodeToOggAsync(dialog.FileName, tempOgg);

            byte[] ogg = await File.ReadAllBytesAsync(tempOgg);
            var vorbis = SbaoAudio.TryReadVorbisId(ogg);
            if (vorbis is not
                { SampleRate: FfmpegAudio.RequiredSampleRate, Channels: FfmpegAudio.RequiredChannels })
            {
                throw new InvalidOperationException(
                    $"ffmpeg produced {vorbis?.SampleRate.ToString() ?? "an unrecognized"} Hz / " +
                    $"{vorbis?.Channels.ToString() ?? "?"} ch, expected {FfmpegAudio.RequiredSampleRate} Hz / " +
                    $"{FfmpegAudio.RequiredChannels} ch.");
            }

            byte[] combined = SbaoAudio.Combine(_header, ogg);

            // Round-trips the freshly built file back through Split as a validity check.
            SbaoAudio.Split(combined);

            _replaceContent(combined);
            Load(combined);
            StatusText.Text += $"\n\nImported from:\n{dialog.FileName}\n\nStaged in your workspace.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't import: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            TryDelete(tempOgg);
            ImportButton.IsEnabled = true;
        }
    }

    private void DeleteTempFiles()
    {
        TryDelete(_tempOggPath);
        TryDelete(_tempWavPath);
        _tempOggPath = null;
        _tempWavPath = null;
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup of our own temp scratch files - a lingering one isn't worth surfacing.
        }
    }
}
