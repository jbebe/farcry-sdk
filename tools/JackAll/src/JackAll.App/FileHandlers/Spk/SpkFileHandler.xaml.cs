using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using JackAll.App.Audio;
using JackAll.Core.Vfs;
using JackAll.Tools.Audio;
using JackAll.Tools.Spk;
using Microsoft.Win32;

namespace JackAll.App.FileHandlers.Spk;

/// <summary>
/// The .spk panel: the bank as a tree of what plays what, an inspector over the selected record, and
/// the problems <c>spk encode</c> would report. Every edit goes through <see cref="SpkBank"/>; the
/// bank is staged once edits settle, unless it has an error.
/// </summary>
/// <remarks>
/// A record sits under whatever plays it, so a bank reads event → container → sample → audio; an id
/// held by another bank is a leaf with "Go to". Imported audio is transcoded to the format of the
/// audio it joins or replaces, and the samples playing it re-derive their lengths from it.
/// </remarks>
public partial class SpkFileHandler : UserControl
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private const string AudioFilter = "Audio files|*.ogg;*.mp3;*.wav;*.flac;*.m4a;*.aac;*.wma;*.opus;*.aiff|All files|*.*";

    private static readonly Dictionary<string, string> FieldLabels = new()
    {
        ["sound"] = "Plays", ["rolloff"] = "Rolloff curve", ["positioned"] = "Positioned", ["gainDb"] = "Gain (dB)", ["audio"] = "Audio",
        ["loop"] = "Loops", ["silence"] = "Silence (%)", ["repeatSilence"] = "Silence may repeat",
        ["sequence"] = "Play in order", ["group"] = "Switch group", ["default"] = "Default",
        ["stop"] = "Stops", ["play"] = "Then plays", ["effect"] = "Reverb effect", ["target"] = "Target",
    };

    /// <summary>One editable curve of a record: a rolloff's, or one of a multilayer layer's.</summary>
    private sealed record Curve(string Name, List<SpkPoint> Points, string X, string Y);

    private readonly string _fileName;
    private readonly Action<byte[]> _replaceContent;
    private readonly Func<uint, VfsFile?> _resolveSoundId;
    private readonly Action<VfsFile> _navigateTo;
    private readonly SpkBank? _bank;

    /// <summary>The id the game loads this bank by, from a hex file name; null for a bark bank.</summary>
    private readonly uint? _loadId;

    /// <summary>The id new records' ids derive from: <see cref="_loadId"/>, else the first event's.</summary>
    private readonly uint _bankId;

    /// <summary>Stages the bank once edits pause: every stage reindexes the whole game.</summary>
    private readonly DispatcherTimer _stageTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    private SpkNode? _selected;
    private string? _tempWavPath;
    private List<Curve> _curves = [];
    private int _curveIndex;

    public SpkFileHandler(
        string fileName, byte[] content, Action<byte[]> replaceContent,
        Func<uint, VfsFile?> resolveSoundId, Action<VfsFile> navigateTo)
    {
        InitializeComponent();
        _fileName = fileName;
        _replaceContent = replaceContent;
        _resolveSoundId = resolveSoundId;
        _navigateTo = navigateTo;
        _stageTimer.Tick += (_, _) => Stage();

        // Release the temp .wav before deleting it: child and parent Unloaded order isn't guaranteed.
        Unloaded += (_, _) =>
        {
            if (_stageTimer.IsEnabled)
            {
                Stage();
            }
            AudioPreview.Reset();
            DeleteTempFile();
        };

        try
        {
            _bank = SpkBank.Parse(content);
        }
        catch (Exception ex)
        {
            HeaderText.Text = $"Couldn't read this file: {ex.Message}";
            return;
        }

        _loadId = SpkBank.LoadIdOf(fileName);
        _bankId = _loadId ?? _bank.Records.FirstOrDefault(r => r.IsEvent)?.Id ?? _bank.Records.FirstOrDefault()?.Id ?? 0;
        ShowProblems();
        Refresh(null);
    }

    private void Refresh(uint? select)
    {
        HeaderText.Text = $"{_fileName} — {_bank!.Records.Count} record(s)";
        List<SpkNode> roots = BuildTree();
        BankTree.ItemsSource = roots;
        if (((select is { } id ? SpkNode.Select(roots, id) : null) ?? roots.FirstOrDefault()) is { } node)
        {
            node.IsSelected = true;
        }
    }

    // --- tree ------------------------------------------------------------------------------------

    /// <summary>Every record nothing in the bank plays is a root; the rest sit under what plays them.</summary>
    private List<SpkNode> BuildTree()
    {
        var played = _bank!.Records.SelectMany(r => r.References()).Select(l => l.Id).ToHashSet();
        return [.. _bank.Records.Where(r => !played.Contains(r.Id)).Select(r => Node(r, [], ""))];
    }

    private SpkNode Node(SpkBankRecord record, HashSet<uint> path, string role)
    {
        var node = new SpkNode
        {
            Record = record, Label = $"{record.Element} 0x{record.Id:x8}",
            Detail = Join(role, Describe(record)), IsExpanded = path.Count < 3,
        };
        if (!path.Add(record.Id))
        {
            return node;
        }

        foreach ((uint id, SpkReference kind) in record.References())
        {
            string childRole = RoleOf(record, id);
            node.Add(_bank!.Find(id) is { } child ? Node(child, [.. path], childRole) : Outside(id, kind, childRole));
        }
        return node;
    }

    private SpkNode Outside(uint id, SpkReference kind, string role)
    {
        VfsFile? file = kind == SpkReference.Rolloff ? null : _resolveSoundId(id);
        string where = kind == SpkReference.Rolloff ? $"the rolloff pack, {SpkBank.RolloffPackPath}"
            : file is not null ? file.Path
            : "another bank, not in the loaded game files";
        return new SpkNode { External = file, Label = $"0x{id:x8}", Detail = Join(role, $"{kind} in {where}") };
    }

    /// <summary>What a child is to a container: its chance, or the switch value it answers.</summary>
    private static string RoleOf(SpkBankRecord parent, uint child)
    {
        int index = parent.Entries.FindIndex(e => e.Ref == child);
        return index < 0 ? ""
            : parent.Layout == SpkLayout.Random ? $"{SpkBankEdits.Chance(parent, index):P0}"
            : parent.Layout?.Children is { ValueAt: >= 0 } ? $"when 0x{parent.Entries[index].Value:x8}"
            : "";
    }

    private static string Join(params string[] parts) => string.Join(" · ", parts.Where(p => p.Length > 0));

    private static string Describe(SpkBankRecord record)
    {
        SpkLayout? layout = record.Layout;
        int children = record.Entries.Count + record.Layers.Count;
        return record switch
        {
            { Raw: true } => $"{record.Data.Length:N0} bytes, not decoded",
            { IsAudio: true } => DescribeAudio(record),
            { IsRolloff: true } => record.Points.Count == 0 ? "no points" : $"gone at {record.Points[^1].X:0.#} m",
            _ when layout == SpkLayout.Play => Join(
                record.Word(SpkLayout.PlayPositioned) == 1 ? "positioned" : "unpositioned",
                record.Word(SpkLayout.PlayRolloff) != SpkLayout.NoId ? $"rolloff 0x{record.Word(SpkLayout.PlayRolloff):x8}" : ""),
            _ when layout == SpkLayout.Sample => Join(
                record.Word(SpkLayout.SampleGain) != 0 ? $"{SpkLayout.FromQ16(record.Word(SpkLayout.SampleGain)):0.#} dB" : "",
                record.Word(SpkLayout.SampleLoop) == 1 ? "loops" : ""),
            _ when layout == SpkLayout.Random => record.Word(10) == 1 ? $"{children} in order"
                : Join($"{children} choices", record.Word(8) != 0 ? $"{SpkBankEdits.Chance(record, null):P0} silence" : ""),
            _ when layout == SpkLayout.Switch || layout == SpkLayout.SwitchEvent => $"{children} cases",
            _ when layout == SpkLayout.MultiEvent => $"starts {children} together",
            _ when layout == SpkLayout.Multilayer => $"{children} layers",
            _ => layout?.KindAttribute is not null ? $"{layout.KindAttribute} {record.Kind}" : "",
        };
    }

    private static string DescribeAudio(SpkBankRecord audio)
    {
        if (SpkBank.DescribeAudio(audio) is not { } info)
        {
            return "not Ogg Vorbis or IMA-ADPCM";
        }
        string channels = info.Channels == 1 ? "Mono" : info.Channels == 2 ? "Stereo" : $"{info.Channels} ch";
        string length = info.SampleRate > 0 ? $"{info.Frames / (double)info.SampleRate:0.00} s" : "";
        return Join(channels, $"{info.SampleRate} Hz", info.Ogg ? "Ogg Vorbis" : "IMA-ADPCM", length,
            MainViewModel.FormatSize(audio.Data.Length));
    }

    // --- inspector -------------------------------------------------------------------------------

    private void BankTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selected = e.NewValue as SpkNode;
        ShowInspector();
    }

    /// <summary>The audio a sample or audio record plays in this bank.</summary>
    private SpkBankRecord? AudioOf(SpkBankRecord? record) =>
        record is { IsAudio: true } ? record
        : record?.Layout == SpkLayout.Sample && _bank!.Find(record.Word(SpkLayout.SampleAudio)) is { IsAudio: true } audio ? audio
        : null;

    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void ShowInspector()
    {
        AudioPreview.Reset();
        DeleteTempFile();
        SpkBankRecord? record = _selected?.Record;
        SpkLayout? layout = record?.Layout;
        SpkBankRecord? audio = AudioOf(record);

        InspectorTitle.Text = _selected?.Label ?? "";
        InspectorDetail.Text = _selected?.Detail ?? "";
        GoToButton.Visibility = Vis(_selected?.External is not null);
        ChoicesSection.Visibility = Vis(layout == SpkLayout.Random);
        AddVariationButton.Visibility = Vis(record is not null && _bank!.SoundFor(record) is not null);
        bool cases = layout?.Children is not null && layout != SpkLayout.Random;
        CasesSection.Visibility = Vis(cases);
        CurveSection.Visibility = Vis(record is { IsRolloff: true } || layout == SpkLayout.Multilayer);
        AudioPanel.Visibility = Vis(record is not null && _bank!.PickAudio(record) is not null);
        PlayPickButton.Visibility = Vis(audio is null);
        ExportAudioButton.Visibility = ImportAudioButton.Visibility = Vis(audio is not null);

        FieldsList.ItemsSource = record is null ? null : Fields(record);
        ChoicesGrid.ItemsSource = layout == SpkLayout.Random ? Choices(record!) : null;
        CasesGrid.ItemsSource = cases ? Cases(record!) : null;
        ShowCurves(record);
        ShowRaw(record);
        if (audio is not null)
        {
            _ = PreparePreviewAsync(audio);
        }
    }

    private List<SpkFieldRow> Fields(SpkBankRecord record)
    {
        var rows = new List<SpkFieldRow>();
        if (record.IsAudio && SpkBank.DescribeAudio(record) is { Ogg: false })
        {
            rows.Add(new SpkFieldRow("Sample rate (Hz)", () => record.SampleRate?.ToString(Invariant) ?? "",
                text => Edit(() => record.SampleRate = int.Parse(text, Invariant))));
        }
        if (record.Layout is not { } layout)
        {
            return rows;
        }

        foreach (SpkWordField field in layout.Fields)
        {
            int index = field.Index;
            string label = FieldLabels.GetValueOrDefault(field.Name, field.Name);
            rows.Add(field.Format switch
            {
                SpkWordFormat.Bool => new SpkFieldRow(label, () => (record.Word(index) == 1).ToString(),
                    text => Edit(() => record.Words[index] = bool.Parse(text) ? 1u : 0u)) { IsBool = true },
                SpkWordFormat.Q16 => new SpkFieldRow(label, () => SpkLayout.FromQ16(record.Word(index)).ToString("0.###", Invariant),
                    text => Edit(() => record.Words[index] = SpkLayout.ToQ16(ParseNumber(text)))),
                SpkWordFormat.Weight => new SpkFieldRow(label, () => Percent(SpkBankEdits.Chance(record, null)),
                    text => Edit(() => SpkBankEdits.SetChance(record, null, ParseNumber(text) / 100))),
                _ => new SpkFieldRow(label, () => FormatId(record.Word(index)),
                    text => Edit(() => record.Words[index] = ParseId(text, layout.Default(index)))),
            });
        }
        return rows;
    }

    private List<SpkChoiceRow> Choices(SpkBankRecord random) =>
        [.. random.Entries.Select((entry, i) =>
        {
            SpkBankRecord? child = _bank!.Find(entry.Ref);
            return new SpkChoiceRow(
                child is null ? $"0x{entry.Ref:x8}" : $"{child.Element} 0x{child.Id:x8}",
                child is null ? "in another bank" : Describe(child),
                () => Percent(SpkBankEdits.Chance(random, i)),
                text => Edit(() => SpkBankEdits.SetChance(random, i, ParseNumber(text) / 100)),
                entry.Extra == 1,
                repeat => Edit(() => random.Entries[i] = random.Entries[i] with { Extra = repeat ? 1u : 0u }));
        })];

    private List<SpkCaseRow> Cases(SpkBankRecord record)
    {
        bool keyed = record.Layout!.Children!.ValueAt >= 0;
        CasesTitle.Text = keyed ? "Cases" : "Starts together";
        CaseValueColumn.Visibility = Vis(keyed);
        return [.. record.Entries.Select((_, i) => new SpkCaseRow(
            () => FormatId(record.Entries[i].Ref),
            text => Edit(() => record.Entries[i] = record.Entries[i] with { Ref = ParseId(text, 0) }),
            keyed ? () => FormatId(record.Entries[i].Value) : null,
            keyed ? text => Edit(() => record.Entries[i] = record.Entries[i] with { Value = ParseId(text, 0) }) : null))];
    }

    private void AddCase_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Record is { } record)
        {
            Edit(() => record.Entries.Add(new SpkEntry(0)));
        }
    }

    private void RemoveCase_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Record is { } record && CasesGrid.SelectedIndex is >= 0 and var index)
        {
            Edit(() => record.Entries.RemoveAt(index));
        }
    }

    // --- curves ----------------------------------------------------------------------------------

    /// <summary>Lists a record's curves in the picker, which draws the one picked.</summary>
    private void ShowCurves(SpkBankRecord? record)
    {
        _curves = record is null ? []
            : record.IsRolloff ? [new Curve("Rolloff", record.Points, "Distance (m)", "dB")]
            : [.. record.Layers.SelectMany((layer, l) => layer.Curves.Select(curve => new Curve(
                $"Layer {l + 1} · {(curve.Target == 1 ? "pitch" : "volume")} on 0x{curve.Parameter:x8}",
                curve.Points, "Parameter", curve.Target == 1 ? "Pitch ratio" : "dB")))];
        CurvePicker.Visibility = Vis(_curves.Count > 1);
        CurvePicker.ItemsSource = _curves;
        CurvePicker.SelectedIndex = _curves.Count == 0 ? -1 : Math.Min(_curveIndex, _curves.Count - 1);
    }

    private void CurvePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CurvePicker.SelectedItem is not Curve curve)
        {
            CurveChart.Show([]);
            PointsGrid.ItemsSource = null;
            return;
        }

        _curveIndex = CurvePicker.SelectedIndex;
        List<SpkPoint> points = curve.Points;
        PointXColumn.Header = curve.X;
        PointYColumn.Header = curve.Y;
        CurveChart.Show([new CurveSeries([.. points.Select(p => new Point(p.X, p.Y))], "AccentBrush")]);
        PointsGrid.ItemsSource = points.Select((_, i) => new SpkPointRow(
            () => points[i].X.ToString("0.###", Invariant),
            () => points[i].Y.ToString("0.###", Invariant),
            (x, y) => Edit(() => points[i] = new SpkPoint((float)ParseNumber(x), (float)ParseNumber(y))))).ToList();
    }

    private void AddPoint_Click(object sender, RoutedEventArgs e)
    {
        if (CurvePicker.SelectedItem is Curve { Points: var points })
        {
            SpkPoint next = points is [.., var last] ? new SpkPoint(last.X + 10, last.Y) : new SpkPoint(0, 0);
            Edit(() => points.Add(next));
        }
    }

    private void RemovePoint_Click(object sender, RoutedEventArgs e)
    {
        if (CurvePicker.SelectedItem is Curve { Points: var points } && PointsGrid.SelectedIndex is >= 0 and var index)
        {
            Edit(() => points.RemoveAt(index));
        }
    }

    // --- variations ------------------------------------------------------------------------------

    private void RemoveChoice_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Record is not { } random)
        {
            return;
        }
        if (ChoicesGrid.SelectedIndex is not (>= 0 and var index))
        {
            StatusText.Text = "Select a variation to remove first.";
            return;
        }
        Edit(() => _bank!.RemoveChoice(random, index));
    }

    /// <summary>Adds one choice per picked file, each transcoded to the format of the sample it joins;
    /// a Play over a lone sample gets a random container around it first.</summary>
    private async void AddVariation_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Record is not { } owner || _bank!.SoundFor(owner) is not { } sound)
        {
            return;
        }

        var dialog = new OpenFileDialog { Title = "Add variations - any format ffmpeg supports", Filter = AudioFilter, Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        SpkBankRecord? template = sound.Layout == SpkLayout.Random ? _bank!.TemplateSample(sound) : sound;
        SpkBankRecord? like = AudioOf(template);

        AddVariationButton.IsEnabled = false;
        try
        {
            var encoded = new List<(byte[] Stream, int? Rate)>();
            foreach (string file in dialog.FileNames)
            {
                StatusText.Text = $"Encoding {Path.GetFileName(file)}…";
                encoded.Add(await EncodeLikeAsync(like, file));
            }
            EditSelecting(() =>
            {
                SpkBank bank = _bank!;
                SpkBankRecord random = bank.RandomFor(owner, _bankId);
                encoded.ForEach(clip => bank.AddVariation(random, clip.Stream, clip.Rate, _bankId));
                return random.Id;
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't add a variation: {ex.Message}";
        }
        finally
        {
            AddVariationButton.IsEnabled = true;
        }
    }

    /// <summary>Transcodes a file to the codec, rate and channels of <paramref name="like"/>, or to
    /// 44.1 kHz mono IMA-ADPCM when there is nothing to match.</summary>
    private static async Task<(byte[] Stream, int? Rate)> EncodeLikeAsync(SpkBankRecord? like, string source)
    {
        var info = SpkBank.DescribeAudio(like);
        string ogg = SoundPreview.TempPath(".ogg");
        string wav = SoundPreview.TempPath(".wav");
        try
        {
            if (info is { Ogg: true })
            {
                await FfmpegAudio.TranscodeToOggAsync(source, ogg, info.Value.SampleRate, info.Value.Channels);
                return (await File.ReadAllBytesAsync(ogg), null);
            }

            int rate = info is { SampleRate: > 0 } ? info.Value.SampleRate : 44100;
            await FfmpegAudio.TranscodeToPcmWavAsync(source, wav, rate, info?.Channels ?? 1);
            WavAudio.Pcm16Audio pcm = WavAudio.ReadPcm16(await File.ReadAllBytesAsync(wav));
            return (ImaAdpcm.Encode(pcm.Samples, pcm.Channels), pcm.SampleRate);
        }
        finally
        {
            SoundPreview.TryDelete(ogg);
            SoundPreview.TryDelete(wav);
        }
    }

    // --- audio -----------------------------------------------------------------------------------

    private async Task PreparePreviewAsync(SpkBankRecord audio)
    {
        try
        {
            string wav = await SoundPreview.AudioToTempWavAsync(audio);
            if (!IsLoaded || AudioOf(_selected?.Record) != audio)
            {
                SoundPreview.TryDelete(wav);
                return;
            }
            _tempWavPath = wav;
            AudioPreview.Open(wav);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't decode this audio: {ex.Message}";
        }
    }

    /// <summary>Plays what the selected record reaches, a random container picking afresh each time.</summary>
    private void PlayPick_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.Record is { } record && _bank!.PickAudio(record) is { } audio)
        {
            AudioPreview.Play(() => SoundPreview.AudioToTempWavAsync(audio));
        }
    }

    private void ExportAudio_Click(object sender, RoutedEventArgs e)
    {
        if (AudioOf(_selected?.Record) is not { } audio)
        {
            return;
        }

        bool ogg = SpkBank.DescribeAudio(audio) is { Ogg: true };
        var dialog = new SaveFileDialog
        {
            Title = "Export audio",
            FileName = $"{audio.Id:x8}{(ogg ? ".ogg" : ".wav")}",
            Filter = ogg ? "Ogg Vorbis file|*.ogg" : "WAV file|*.wav",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            // An Ogg stream already is a complete file: exported as is rather than re-encoded.
            File.WriteAllBytes(dialog.FileName, ogg ? audio.Data : SoundPreview.ImaAdpcmToWav(audio));
            StatusText.Text = $"Exported to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't export: {ex.Message}";
        }
    }

    private async void ImportAudio_Click(object sender, RoutedEventArgs e)
    {
        if (AudioOf(_selected?.Record) is not { } audio)
        {
            return;
        }

        var dialog = new OpenFileDialog { Title = "Import replacement audio - any format ffmpeg supports", Filter = AudioFilter };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        ImportAudioButton.IsEnabled = false;
        try
        {
            StatusText.Text = "Encoding…";
            (byte[] stream, int? rate) = await EncodeLikeAsync(audio, dialog.FileName);
            Edit(() => _bank.ReplaceAudio(audio, stream, rate));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't import: {ex.Message}";
        }
        finally
        {
            ImportAudioButton.IsEnabled = true;
        }
    }

    // --- raw details -----------------------------------------------------------------------------

    /// <summary>Every word by index: unnamed ones editable, derived ones as written.</summary>
    private void ShowRaw(SpkBankRecord? record)
    {
        RawFieldsList.ItemsSource = null;
        if (record is null)
        {
            RawDetailsText.Text = "";
            return;
        }

        var text = new StringBuilder();
        if (record.Layout is { } layout)
        {
            uint[] written = _bank!.DerivedWords(record);
            RawFieldsList.ItemsSource = Enumerable.Range(0, written.Length)
                .Where(i => !layout.Fields.Any(f => f.Index == i))
                .Select(i => layout.Derived.Contains(i)
                    ? new SpkFieldRow($"[{i}] derived", () => $"0x{written[i]:x8}", null)
                    : new SpkFieldRow($"[{i}]", () => $"0x{record.Word(i):x8}", value => Edit(() => record.Words[i] = ParseId(value, 0))))
                .ToList();
            if (record.Pins.Count > 0)
            {
                text.AppendLine($"pinned: {string.Join(", ", record.Pins.Select(p => $"[{p.Key}]=0x{p.Value:x8}"))}");
            }
            if (record.Tail is { } tail)
            {
                text.AppendLine($"tail kept as stored: {tail.Length:N0} bytes");
            }
        }

        text.AppendLine($"preamble: {string.Join(" ", (record.Preamble ?? _bank!.Preamble).Select(w => $"0x{w:x8}"))}");
        if (record.Key is { } key)
        {
            text.AppendLine($"core key: {Convert.ToHexString(key)}");
        }
        if (record.IsAudio || record.Raw)
        {
            text.AppendLine($"{(record.Raw ? "payload" : "stream")}: {record.Data.Length:N0} bytes, " +
                            $"starting {Convert.ToHexString(record.Data.AsSpan(0, Math.Min(16, record.Data.Length)))}");
        }
        RawDetailsText.Text = text.ToString().TrimEnd();
    }

    // --- editing ---------------------------------------------------------------------------------

    private void Edit(Action change) => EditSelecting(() =>
    {
        change();
        return (uint?)null;
    });

    /// <summary>
    /// Applies a change, shows the bank as it now is and schedules staging. The rebuild waits for the
    /// input that caused it to finish; <paramref name="change"/> may name the record to select.
    /// </summary>
    private void EditSelecting(Func<uint?> change)
    {
        uint? keep = _selected?.Record?.Id;
        uint? select;
        try
        {
            select = change() ?? keep;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            Dispatcher.BeginInvoke(() => Refresh(keep));
            return;
        }

        StatusText.Text = "Staging…";
        _stageTimer.Stop();
        _stageTimer.Start();
        Dispatcher.BeginInvoke(() => Refresh(select));
    }

    /// <summary>Stages the bank unless it has an error; the host keeps this panel over the file it staged.</summary>
    private void Stage()
    {
        _stageTimer.Stop();
        if (!ShowProblems())
        {
            StatusText.Text = "Not staged: fix the errors below first.";
            return;
        }

        byte[] bytes = _bank!.Write();
        // A read-back check: a bank that fails to parse is never staged.
        SpkBank.Parse(bytes);
        _replaceContent(bytes);
        StatusText.Text = "Staged in your workspace.";
    }

    /// <summary>Lists errors and warnings, and counts the ids other banks must supply; false on an error.</summary>
    private bool ShowProblems()
    {
        IReadOnlyList<SpkProblem> problems = SpkBankLint.Check(_bank!, _loadId);
        var lines = problems.Where(p => p.Severity != SpkProblemSeverity.Note)
            .Select(p => $"{(p.Severity == SpkProblemSeverity.Error ? "Error" : "Warning")}: {p.Message}").ToList();
        int notes = problems.Count(p => p.Severity == SpkProblemSeverity.Note);
        if (notes > 0)
        {
            lines.Add($"{notes} reference(s) point into other banks, which must be loaded for them to play.");
        }
        ProblemsList.ItemsSource = lines;
        ProblemsPanel.Visibility = Vis(lines.Count > 0);
        return problems.All(p => p.Severity != SpkProblemSeverity.Error);
    }

    private void GoTo_Click(object sender, RoutedEventArgs e)
    {
        if (_selected?.External is { } file)
        {
            _navigateTo(file);
        }
    }

    // --- formatting ------------------------------------------------------------------------------

    private static string Percent(double chance) => (chance * 100).ToString("0.##", Invariant);

    private static string FormatId(uint id) => id == SpkLayout.NoId ? "" : $"0x{id:x8}";

    /// <summary>A hex id; blank is the field's default.</summary>
    private static uint ParseId(string text, uint blank) => string.IsNullOrWhiteSpace(text) ? blank : SpkBank.ParseId(text);

    private static double ParseNumber(string text) =>
        double.TryParse(text.Trim().TrimEnd('%').Replace(',', '.'), NumberStyles.Float, Invariant, out double value)
            ? value
            : throw new FormatException($"'{text}' is not a number.");

    private void DeleteTempFile()
    {
        SoundPreview.TryDelete(_tempWavPath);
        _tempWavPath = null;
    }
}
