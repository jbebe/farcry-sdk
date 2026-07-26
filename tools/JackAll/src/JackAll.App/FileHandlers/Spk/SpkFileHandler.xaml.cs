using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using JackAll.App.Audio;
using JackAll.Core.Format;
using JackAll.Core.Vfs;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Spk;

/// <summary>
/// The file handler for .spk sound-bank containers. One row per record in a plain, decoded table -
/// no raw hex up front: a `FlatCopy` record (the one holding actual audio) shows its format/duration/
/// size, and each metadata record (`SimpleFixed68`/`TransformedFixed128`) shows which other record it
/// points to and its gain, in plain language. Every record's own byte-level fields (the confirmed
/// constants, the four still-unidentified core fields, full sub-header words, preamble) are still
/// available - just behind the "Show raw technical details" checkbox for the currently selected row,
/// rather than always on screen. See <see cref="SpkPackage"/>'s remarks for what each field means and
/// how confident that meaning is.
///
/// Selecting a row with audio drives the play/export/import panel directly (no separate picker) -
/// decoded and encoded on the fly with <see cref="ImaAdpcm"/>, the same pattern <c>SbaoFileHandler</c>
/// uses for Ogg-backed `.sbao`, with ffmpeg doing only format/rate/channel transcoding to and from raw
/// PCM. Importing replaces only the selected record's payload via
/// <see cref="SpkPackage.ReplaceRecordPayload"/> - everything else in the file is carried forward
/// byte-for-byte, and the replacement audio is transcoded to whatever sample rate/channel count that
/// record already used.
///
/// A `SimpleFixed68`/`TransformedFixed128` row's own cross-reference (`LinkedId`/`FlatCopySiblingId`)
/// gets a "Go to" action, the same jump mechanism <c>DependencyLinkHandler</c> uses for `.fcb`
/// references: if the id matches another record already in this same bank, it just selects that row;
/// otherwise it's looked up VFS-wide (<c>resolveByHash</c>) - this is exactly how the no-audio-of-its-
/// own "alias" banks work (see docs/docs/file-formats/spk.md), which point at a completely different
/// `.spk` file's `TransformedFixed128`/`FlatCopy` pair rather than anything in their own single-record
/// bank.
/// </summary>
public partial class SpkFileHandler : UserControl
{
    private const int PayloadPreviewBytes = 16;
    private const int FallbackSampleRateHz = 32000; // most common real-install TransformedFixed128 rate

    private readonly string _fileName;
    private readonly Action<byte[]> _replaceContent;
    private readonly Func<uint, VfsFile?> _resolveByHash;
    private readonly Action<VfsFile> _navigateTo;
    private readonly DispatcherTimer _timer;
    private SpkPackage? _package;
    private byte[]? _originalContent;
    private List<Row> _rows = [];
    private string? _tempWavPath;
    private bool _isUserSeeking;
    private bool _updatingSlider;

    /// <summary>One row of the records grid - a plain-language view of a single record, decoded as far
    /// as we understand its type. <see cref="LinkedId"/> is this record's own outgoing cross-reference
    /// (a `SimpleFixed68`'s `LinkedId` or a `TransformedFixed128`'s `FlatCopySiblingId`), if it has one;
    /// <see cref="LinkedInSameFile"/>/<see cref="LinkedExternalFile"/> say where (if anywhere) it
    /// actually resolves to, computed once when the row is built.</summary>
    private sealed class Row
    {
        public required int DisplayIndex { get; init; }
        public required string IdHex { get; init; }
        public required string Kind { get; init; }
        public required string Summary { get; init; }
        public required SpkRecord Record { get; init; }
        public required bool IsPlayable { get; init; }
        public required uint? LinkedId { get; init; }
        public required bool LinkedInSameFile { get; init; }
        public required VfsFile? LinkedExternalFile { get; init; }

        public bool HasLink => LinkedId is not null;
        public bool CanNavigateLink => LinkedInSameFile || LinkedExternalFile is not null;
        public string LinkButtonText => LinkedId is null ? "" : (CanNavigateLink ? "Go to →" : "Not found");
    }

    public SpkFileHandler(
        string fileName, byte[] content, Action<byte[]> replaceContent,
        Func<uint, VfsFile?> resolveByHash, Action<VfsFile> navigateTo)
    {
        InitializeComponent();
        _fileName = fileName;
        _replaceContent = replaceContent;
        _resolveByHash = resolveByHash;
        _navigateTo = navigateTo;

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

            _rows = BuildRows(_package);
            HeaderText.Text = $"{_fileName} — {_package.Records.Count} record(s)";
            RecordsGrid.ItemsSource = _rows;
            AudioPanel.Visibility = _rows.Any(r => r.IsPlayable) ? Visibility.Visible : Visibility.Collapsed;

            Row? toSelect = reselectRecordId is { } id
                ? _rows.FirstOrDefault(r => r.Record.Id == id)
                : _rows.FirstOrDefault(r => r.IsPlayable);
            RecordsGrid.SelectedItem = toSelect; // triggers SelectionChanged -> loads the preview, if any
        }
        catch (Exception ex)
        {
            _package = null;
            _originalContent = null;
            _rows = [];
            HeaderText.Text = $"Couldn't read this file: {ex.Message}";
            RecordsGrid.ItemsSource = null;
            AudioPanel.Visibility = Visibility.Collapsed;
            ResetPlayer();
        }
    }

    private List<Row> BuildRows(SpkPackage package)
    {
        var rows = new List<Row>(package.Records.Count);
        for (int i = 0; i < package.Records.Count; i++)
        {
            SpkRecord r = package.Records[i];
            uint? linkedId = r.TransformedFixed128?.FlatCopySiblingId ?? r.SimpleFixed68?.LinkedId;
            bool linkedInSameFile = linkedId is { } id && package.Records.Any(other => other.Id == id);
            VfsFile? linkedExternalFile = linkedId is { } id2 && !linkedInSameFile ? _resolveByHash(id2) : null;

            rows.Add(new Row
            {
                DisplayIndex = i + 1,
                IdHex = $"0x{r.Id:x8}",
                Kind = DescribeKind(r),
                Summary = DescribeSummary(package, r),
                Record = r,
                IsPlayable = r.FlatCopyAudioStream is not null,
                LinkedId = linkedId,
                LinkedInSameFile = linkedInSameFile,
                LinkedExternalFile = linkedExternalFile,
            });
        }

        return rows;
    }

    private void GoToLink_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not Row row)
        {
            return;
        }

        if (row.LinkedInSameFile && row.LinkedId is { } id)
        {
            RecordsGrid.SelectedItem = _rows.FirstOrDefault(r => r.Record.Id == id);
        }
        else if (row.LinkedExternalFile is { } file)
        {
            _navigateTo(file);
        }
    }

    private static string DescribeKind(SpkRecord r) => r.Core switch
    {
        null => "(malformed)",
        { Type: SpkRecordType.FlatCopy } => "▶ Audio",
        { Type: SpkRecordType.TransformedFixed128 } => "Audio params",
        { Type: SpkRecordType.SimpleFixed68 } => "Sound params",
        { Type: { } t } => t.ToString(),
        _ => $"Unknown (0x{r.Core.RawType:x8})",
    };

    private string DescribeSummary(SpkPackage package, SpkRecord r)
    {
        if (r.FlatCopyAudioStream is { } audio)
        {
            try
            {
                ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(audio);
                int? sampleRate = package.TryGetFlatCopySampleRate(r);
                int rate = sampleRate ?? FallbackSampleRateHz;
                int frames = decoded.Samples.Length / decoded.Channels;
                string channelLabel = decoded.Channels == 2 ? "Stereo" : "Mono";
                string rateLabel = sampleRate is { } hz ? $"{hz} Hz" : $"~{FallbackSampleRateHz} Hz (no rate on record)";
                return $"{channelLabel} · {rateLabel} · {FormatTime(TimeSpan.FromSeconds((double)frames / rate))} · {FormatBytes(audio.Length)}";
            }
            catch (Exception ex)
            {
                return $"⚠ couldn't decode: {ex.Message}";
            }
        }

        if (r.TransformedFixed128 is { } t128)
        {
            return $"→ audio 0x{t128.FlatCopySiblingId:x8} · {t128.SampleRate} Hz · gain {FormatQ16_16(t128.GainQ16_16)}";
        }

        if (r.SimpleFixed68 is { } s68)
        {
            var extras = new List<string>();
            if (s68.SignedHundredFlag != 0)
            {
                extras.Add($"flag {s68.SignedHundredFlag}");
            }

            if (s68.BoolFlag != 0)
            {
                extras.Add("flagged");
            }

            string extra = extras.Count > 0 ? " · " + string.Join(" · ", extras) : "";
            return $"→ 0x{s68.LinkedId:x8} · gain {FormatQ16_16(unchecked((int)s68.IdentityGainQ16_16))}{extra}";
        }

        return r.Core is null
            ? "too short for the 40-byte record core"
            : $"{r.Payload.Length:N0} bytes";
    }

    private static string FormatQ16_16(int fixedPoint) => (fixedPoint / 65536.0).ToString("0.###");

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024.0):0.#} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }

        return $"{bytes} B";
    }

    private void RecordsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Row? row = RecordsGrid.SelectedItem as Row;
        UpdateRawDetails(row);

        if (row is not { IsPlayable: true })
        {
            ResetPlayer();
            ImportAudioButton.IsEnabled = false;
            ExportAudioButton.IsEnabled = false;
            return;
        }

        ImportAudioButton.IsEnabled = true;
        _ = PreparePreviewAsync(row.Record);
    }

    private void ShowRawDetailsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        RawDetailsPanel.Visibility = ShowRawDetailsCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRawDetails(Row? row)
    {
        RawDetailsText.Text = row is null ? "(no record selected)" : BuildRawDetails(row.DisplayIndex - 1, row.Record);
    }

    /// <summary>The full byte-level breakdown for one record - every core/sub-header field (including
    /// the ones folded into <see cref="DescribeSummary"/> already, plus the still-unidentified ones),
    /// preamble words, and a hex preview for anything we don't have a decoded meaning for at all.</summary>
    private static string BuildRawDetails(int index, SpkRecord r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{index}] id=0x{r.Id:x8}  size={r.Payload.Length:N0}");
        sb.AppendLine($"preamble=[{string.Join(", ", r.PreambleWords.Select(w => $"0x{w:x8}"))}]");

        if (r.Core is not { } core)
        {
            sb.Append("(too short for the 40-byte record core)");
            return sb.ToString();
        }

        string typeName = core.Type?.ToString() ?? $"unknown (0x{core.RawType:x8})";
        sb.AppendLine(
            $"type={typeName}" +
            (core.HasStandardDeclaredSize ? "" : $"  !! declaredSize=0x{core.DeclaredSize:x} (expected 0x28)") +
            $"  unknown=[0x{core.Unknown08:x8}, 0x{core.Unknown0C:x8}, 0x{core.Unknown10:x8}, 0x{core.Unknown14:x8}]" +
            $"  reserved=[0x{core.ReservedZero18:x8}, 0x{core.ReservedZero1C:x8}, 0x{core.ReservedTwo24:x8}]");

        if (r.SimpleFixed68 is { } s68)
        {
            sb.AppendLine(
                $"SimpleFixed68: ownId=0x{s68.OwnId:x8}  linkedId=0x{s68.LinkedId:x8}  categoryId=0x{s68.CategoryId:x8}  " +
                $"gain={FormatQ16_16(unchecked((int)s68.IdentityGainQ16_16))}  variant={s68.VariantOrVoiceCount}  " +
                $"flag100={s68.SignedHundredFlag}  bool={s68.BoolFlag}");
        }

        if (r.TransformedFixed128 is { } t128)
        {
            sb.AppendLine(
                $"TransformedFixed128: ownId=0x{t128.OwnId:x8}  flatCopySibling=0x{t128.FlatCopySiblingId:x8}  " +
                $"gain={FormatQ16_16(t128.GainQ16_16)}  channelsGuess={t128.ChannelCountGuess}  " +
                $"sampleRate={t128.SampleRate} Hz  word20={t128.Word20}  word25={t128.Word25}  " +
                $"word28={t128.Word28}  word31=0x{t128.Word31:x8}");
        }

        if (r.FlatCopyAudioStream is { } audio)
        {
            sb.Append($"FlatCopy audio stream: {audio.Length:N0} bytes (28-byte TImaAdpcm header + nibbles)");
        }
        else
        {
            byte[] remainder = r.Payload[SpkRecordCore.Size..];
            int previewLen = Math.Min(PayloadPreviewBytes, remainder.Length);
            string hex = string.Join(" ", remainder.Take(previewLen).Select(b => b.ToString("x2")));
            sb.Append($"payload (after core): {hex}{(previewLen < remainder.Length ? " ..." : "")}");
        }

        return sb.ToString();
    }

    private async Task PreparePreviewAsync(SpkRecord record)
    {
        ResetPlayer();
        DeleteTempFile();
        ExportAudioButton.IsEnabled = false;
        AudioStatusText.Text = "";

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
            AudioStatusText.Text = $"Couldn't decode this record's audio: {ex.Message}";
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
        if (RecordsGrid.SelectedItem is not Row { IsPlayable: true } row)
        {
            return;
        }

        SpkRecord record = row.Record;
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
            AudioStatusText.Text = $"Exported to:\n{dialog.FileName}\n({sampleRate} Hz)";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't export: {ex.Message}", "JackAll",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ImportAudio_Click(object sender, RoutedEventArgs e)
    {
        if (_package is null || _originalContent is null ||
            RecordsGrid.SelectedItem is not Row { IsPlayable: true } row)
        {
            return;
        }

        SpkRecord record = row.Record;
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

            AudioStatusText.Text = $"Transcoding to {sampleRate} Hz, {channels}-channel PCM…";
            await FfmpegAudio.TranscodeToPcmWavAsync(dialog.FileName, tempWav, sampleRate, channels);

            WavAudio.Pcm16Audio pcm = WavAudio.ReadPcm16(await File.ReadAllBytesAsync(tempWav));
            byte[] encoded = ImaAdpcm.Encode(pcm.Samples, pcm.Channels);
            byte[] newPayload = [.. record.Payload[..SpkRecordCore.Size], .. encoded];

            byte[] patched = SpkPackage.ReplaceRecordPayload(_originalContent, record.Id, newPayload);

            // Round-trips the freshly built file back through Parse as a validity check.
            SpkPackage.Parse(patched);

            _replaceContent(patched);
            Load(patched, reselectRecordId: record.Id);
            AudioStatusText.Text = $"Imported from:\n{dialog.FileName}\n\nStaged in your workspace.";
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
