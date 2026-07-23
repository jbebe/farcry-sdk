using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JackAll.App.Audio;
using JackAll.Core.Format;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Sbao;

/// <summary>
/// The file handler for Ogg-backed .sbao audio (music, dialogue). Splits the file into its
/// engine header and embedded Ogg Vorbis payload on load, offers export as the original Ogg or as
/// mp3, imports any ffmpeg-readable audio (transcoded to Far Cry 2's required 48 kHz stereo Ogg
/// Vorbis and staged into the workspace), and previews it with a small play/pause/stop/seek player.
/// </summary>
public partial class SbaoFileHandler : UserControl
{
    private readonly string _fileName;
    private readonly Action<byte[]> _replaceContent;
    private readonly DispatcherTimer _timer;
    private byte[]? _header;
    private byte[]? _ogg;
    private string? _tempOggPath;
    private string? _tempWavPath;
    private bool _isUserSeeking;
    private bool _updatingSlider;

    public SbaoFileHandler(string fileName, byte[] content, Action<byte[]> replaceContent)
    {
        InitializeComponent();
        _fileName = fileName;
        _replaceContent = replaceContent;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
        Unloaded += (_, _) => Cleanup();

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
            ResetPlayer();
        }
    }

    private async Task PreparePreviewAsync(byte[] ogg)
    {
        ResetPlayer();
        DeleteTempFiles();

        try
        {
            _tempOggPath = Path.Combine(Path.GetTempPath(), $"jackall_sbao_{Guid.NewGuid():N}.ogg");
            _tempWavPath = Path.ChangeExtension(_tempOggPath, ".wav");
            await File.WriteAllBytesAsync(_tempOggPath, ogg);
            await FfmpegAudio.TranscodeToWavAsync(_tempOggPath, _tempWavPath);

            Player.Source = new Uri(_tempWavPath);
            PlayButton.IsEnabled = true;
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

    private void Play_Click(object sender, RoutedEventArgs e) => Player.Play();

    private void Pause_Click(object sender, RoutedEventArgs e) => Player.Pause();

    private void Stop_Click(object sender, RoutedEventArgs e) => Player.Stop();

    private void Player_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (Player.NaturalDuration.HasTimeSpan)
        {
            SeekBar.Maximum = Player.NaturalDuration.TimeSpan.TotalSeconds;
        }

        SeekBar.IsEnabled = true;
        PauseButton.IsEnabled = true;
        StopButton.IsEnabled = true;
    }

    private void Player_MediaEnded(object sender, RoutedEventArgs e) => Player.Stop();

    private void SeekBar_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _isUserSeeking = true;

    private void SeekBar_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isUserSeeking = false;
        Player.Position = TimeSpan.FromSeconds(SeekBar.Value);
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSlider || !_isUserSeeking)
        {
            return;
        }

        TimeText.Text = FormatTime(TimeSpan.FromSeconds(SeekBar.Value)) + " / " + FormatTime(TotalDuration());
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_isUserSeeking || Player.Source is null || !Player.NaturalDuration.HasTimeSpan)
        {
            return;
        }

        _updatingSlider = true;
        SeekBar.Value = Player.Position.TotalSeconds;
        _updatingSlider = false;
        TimeText.Text = FormatTime(Player.Position) + " / " + FormatTime(TotalDuration());
    }

    private TimeSpan TotalDuration()
        => Player.NaturalDuration.HasTimeSpan ? Player.NaturalDuration.TimeSpan : TimeSpan.Zero;

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:D2}";

    private void ResetPlayer()
    {
        Player.Stop();
        Player.Close();
        Player.Source = null;
        SeekBar.Value = 0;
        SeekBar.IsEnabled = false;
        TimeText.Text = "0:00 / 0:00";
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
    }

    private void Cleanup()
    {
        _timer.Stop();
        ResetPlayer();
        DeleteTempFiles();
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
