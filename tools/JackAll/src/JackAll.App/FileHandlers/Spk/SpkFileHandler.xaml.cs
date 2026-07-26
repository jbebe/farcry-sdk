using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JackAll.App.Audio;
using JackAll.Core.Format;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Spk;

/// <summary>
/// The file handler for .spk sound-bank containers. Shows every record's id, preamble words, 40-byte
/// core fields, and (for the two most common types) sub-header fields - all confirmed by tracing
/// Dunia.dll's real sound-bank loader and codec in Ghidra (see <see cref="SpkPackage"/>'s remarks).
/// `FlatCopy` records (the ones holding actual compressed audio) get a play/export/import preview,
/// decoded and encoded on the fly with <see cref="ImaAdpcm"/> - the same pattern <c>SbaoFileHandler</c>
/// uses for Ogg-backed `.sbao`, with ffmpeg doing only format/rate/channel transcoding to and from raw
/// PCM (there's no container-level codec to shell out to here the way there is for Ogg Vorbis).
///
/// Importing replaces only the selected record's payload - the rest of the file (ids, preamble words,
/// every other record, and this record's own 40-byte core) is carried forward byte-for-byte via
/// <see cref="SpkPackage.ReplaceRecordPayload"/>, and the replacement audio is transcoded to whatever
/// sample rate/channel count that record already used (read from its own current stream and its
/// `TransformedFixed128` sibling), so nothing else about the bank needs to change to match it.
/// </summary>
public partial class SpkFileHandler : UserControl
{
    private const int PayloadPreviewBytes = 16;
    private const int FallbackSampleRateHz = 32000; // most common real-install TransformedFixed128 rate

    private readonly string _fileName;
    private readonly Action<byte[]> _replaceContent;
    private readonly DispatcherTimer _timer;
    private SpkPackage? _package;
    private byte[]? _originalContent;
    private List<SpkRecord> _audioRecords = [];
    private string? _tempWavPath;
    private bool _isUserSeeking;
    private bool _updatingSlider;

    public SpkFileHandler(string fileName, byte[] content, Action<byte[]> replaceContent)
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

    private void Load(byte[] content, uint? reselectRecordId = null)
    {
        try
        {
            _package = SpkPackage.Parse(content);
            _originalContent = content;
            _audioRecords = _package.Records.Where(r => r.FlatCopyAudioStream is not null).ToList();

            StatusText.Text = Describe(_fileName, _package);

            if (_audioRecords.Count > 0)
            {
                AudioPanel.Visibility = Visibility.Visible;
                AudioRecordCombo.ItemsSource = _audioRecords
                    .Select(r => $"0x{r.Id:x8}  ({r.FlatCopyAudioStream!.Length:N0} bytes)")
                    .ToList();
                int index = reselectRecordId is { } id ? _audioRecords.FindIndex(r => r.Id == id) : -1;
                AudioRecordCombo.SelectedIndex = index >= 0 ? index : 0; // triggers SelectionChanged -> loads the preview
            }
            else
            {
                AudioPanel.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _package = null;
            _originalContent = null;
            _audioRecords = [];
            AudioPanel.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Couldn't read this file: {ex.Message}";
        }
    }

    private string Describe(string fileName, SpkPackage package)
    {
        var sb = new StringBuilder();
        sb.AppendLine(fileName);
        sb.AppendLine();
        sb.AppendLine($"Records: {package.Records.Count}");
        sb.AppendLine();

        for (int i = 0; i < package.Records.Count; i++)
        {
            SpkRecord r = package.Records[i];
            sb.AppendLine($"[{i}] id=0x{r.Id:x8}  preamble=[{string.Join(", ", r.PreambleWords.Select(w => $"0x{w:x8}"))}]  size={r.Payload.Length:N0}");

            if (r.Core is not { } core)
            {
                sb.AppendLine("      (too short for the 40-byte record core)");
                continue;
            }

            string typeName = core.Type?.ToString() ?? $"unknown (0x{core.RawType:x8})";
            sb.AppendLine(
                $"      type={typeName}" +
                (core.HasStandardDeclaredSize ? "" : $"  !! declaredSize=0x{core.DeclaredSize:x} (expected 0x28)") +
                $"  unknown=[0x{core.Unknown08:x8}, 0x{core.Unknown0C:x8}, 0x{core.Unknown10:x8}, 0x{core.Unknown14:x8}]");

            if (r.SimpleFixed68 is { } s68)
            {
                sb.AppendLine(
                    $"      SimpleFixed68: linkedId=0x{s68.LinkedId:x8}  categoryId=0x{s68.CategoryId:x8}  " +
                    $"gain={FormatQ16_16(unchecked((int)s68.IdentityGainQ16_16))}  variant={s68.VariantOrVoiceCount}  " +
                    $"flag100={s68.SignedHundredFlag}  bool={s68.BoolFlag}");
            }

            if (r.TransformedFixed128 is { } t128)
            {
                sb.AppendLine(
                    $"      TransformedFixed128: flatCopySibling=0x{t128.FlatCopySiblingId:x8}  " +
                    $"gain={FormatQ16_16(t128.GainQ16_16)}  channelsGuess={t128.ChannelCountGuess}  " +
                    $"sampleRate={t128.SampleRate} Hz  word20={t128.Word20}  word25={t128.Word25}  " +
                    $"word28={t128.Word28}  word31=0x{t128.Word31:x8}");
            }

            if (r.FlatCopyAudioStream is { } audio)
            {
                int? rate = package.TryGetFlatCopySampleRate(r);
                sb.AppendLine($"      FlatCopy audio: {audio.Length:N0} bytes" +
                               (rate is { } hz ? $"  (sibling reports {hz} Hz)" : "  (no sibling rate found)"));
            }
            else
            {
                int previewLen = Math.Min(PayloadPreviewBytes, r.Payload.Length);
                string hex = string.Join(" ", r.Payload.Take(previewLen).Select(b => b.ToString("x2")));
                sb.AppendLine($"      {hex}{(previewLen < r.Payload.Length ? " ..." : "")}");
            }
        }

        return sb.ToString();
    }

    private static string FormatQ16_16(int fixedPoint) => (fixedPoint / 65536.0).ToString("0.###");

    private void AudioRecordCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = AudioRecordCombo.SelectedIndex;
        if (index < 0 || index >= _audioRecords.Count)
        {
            ImportAudioButton.IsEnabled = false;
            return;
        }

        ImportAudioButton.IsEnabled = true;
        _ = PreparePreviewAsync(_audioRecords[index]);
    }

    private async Task PreparePreviewAsync(SpkRecord record)
    {
        ResetPlayer();
        DeleteTempFile();
        ExportAudioButton.IsEnabled = false;

        try
        {
            byte[] wav = DecodeToWav(record, out _);
            _tempWavPath = Path.Combine(Path.GetTempPath(), $"jackall_spk_{Guid.NewGuid():N}.wav");
            await File.WriteAllBytesAsync(_tempWavPath, wav);

            Player.Source = new Uri(_tempWavPath);
            PlayButton.IsEnabled = true;
            ExportAudioButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text += $"\n\nCouldn't decode this record's audio: {ex.Message}";
        }
    }

    private byte[] DecodeToWav(SpkRecord record, out int sampleRate)
    {
        byte[] stream = record.FlatCopyAudioStream
            ?? throw new InvalidOperationException("Selected record has no FlatCopy audio stream.");
        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);
        sampleRate = _package?.TryGetFlatCopySampleRate(record) ?? FallbackSampleRateHz;
        return WavAudio.Write(decoded.Samples, decoded.Channels, sampleRate);
    }

    private void ExportAudio_Click(object sender, RoutedEventArgs e)
    {
        int index = AudioRecordCombo.SelectedIndex;
        if (index < 0 || index >= _audioRecords.Count)
        {
            return;
        }

        SpkRecord record = _audioRecords[index];
        var dialog = new SaveFileDialog
        {
            Title = "Export audio",
            FileName = $"{record.Id:x8}.wav",
            Filter = "WAV file|*.wav",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            byte[] wav = DecodeToWav(record, out int sampleRate);
            File.WriteAllBytes(dialog.FileName, wav);
            StatusText.Text += $"\n\nExported to:\n{dialog.FileName}\n({sampleRate} Hz)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't export: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImportAudio_Click(object sender, RoutedEventArgs e)
    {
        int index = AudioRecordCombo.SelectedIndex;
        if (_package is null || _originalContent is null || index < 0 || index >= _audioRecords.Count)
        {
            return;
        }

        SpkRecord record = _audioRecords[index];
        var dialog = new OpenFileDialog
        {
            Title = "Import replacement audio - any format ffmpeg supports",
            Filter = "Audio files|*.ogg;*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.opus;*.aiff|All files|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        ImportAudioButton.IsEnabled = false;
        string tempWav = Path.Combine(Path.GetTempPath(), $"jackall_spk_import_{Guid.NewGuid():N}.wav");
        try
        {
            // Preserve this record's own current rate/channel count rather than imposing a fixed
            // target - there's no single required format the way .sbao has one, and every other
            // record's metadata (which describes THIS record) would otherwise go stale.
            int channels = ImaAdpcm.Decode(record.FlatCopyAudioStream!).Channels;
            int sampleRate = _package.TryGetFlatCopySampleRate(record) ?? FallbackSampleRateHz;

            StatusText.Text += $"\n\nTranscoding to {sampleRate} Hz, {channels}-channel PCM…";
            await FfmpegAudio.TranscodeToPcmWavAsync(dialog.FileName, tempWav, sampleRate, channels);

            WavAudio.Pcm16Audio pcm = WavAudio.ReadPcm16(await File.ReadAllBytesAsync(tempWav));
            byte[] encoded = ImaAdpcm.Encode(pcm.Samples, pcm.Channels);
            byte[] newPayload = [.. record.Payload[..SpkRecordCore.Size], .. encoded];

            byte[] patched = SpkPackage.ReplaceRecordPayload(_originalContent, record.Id, newPayload);

            // Round-trips the freshly built file back through Parse as a validity check.
            SpkPackage.Parse(patched);

            _replaceContent(patched);
            Load(patched, reselectRecordId: record.Id);
            StatusText.Text += $"\n\nImported from:\n{dialog.FileName}\n\nStaged in your workspace.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't import: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            TryDelete(tempWav);
            ImportAudioButton.IsEnabled = true;
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
        DeleteTempFile();
    }

    private void DeleteTempFile()
    {
        if (_tempWavPath is null)
        {
            return;
        }

        TryDelete(_tempWavPath);
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
            // Best-effort cleanup of our own temp scratch file - a lingering one isn't worth surfacing.
        }
    }
}
