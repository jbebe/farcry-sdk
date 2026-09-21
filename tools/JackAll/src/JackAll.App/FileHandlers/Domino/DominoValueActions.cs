using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using JackAll.App.Audio;
using JackAll.App.FileHandlers.Xbt;
using JackAll.Core.Format.Rml;
using JackAll.Core.Vfs;
using JackAll.Tools.Bark;
using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.Spk;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>One thing the user can do with a value or a box.</summary>
public sealed record DominoAction(string Label, Action Run);

/// <summary>What a <see cref="ValueRef"/> names in the loaded game, and what can be done with it.</summary>
/// <param name="play">Hands the player a label and a way to produce the temp .wav to play.</param>
/// <param name="selectUses">Selects every box in the graph whose value matches.</param>
public sealed class DominoValueActions(
    DominoServices services,
    Action<string, Func<Task<string>>> play,
    Action<ValueRef> selectUses)
{
    private readonly Dictionary<string, ImageSource?> _thumbnails = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts reading the bark banks; <paramref name="ready"/> runs on the calling thread once they are in.</summary>
    public void LoadBarks(Action ready) =>
        services.Barks.Value.ContinueWith(_ => ready(), TaskScheduler.FromCurrentSynchronizationContext());

    /// <summary>What a parameter holds, in words: where its value comes from and what it names.
    /// Null when the raw expression already says it all.</summary>
    public string? Explain(ExpressionSyntax expr, ValueRef? value, ReconstructedGraph graph)
    {
        string? origin = DominoValueRefs.Resolve(expr, graph) switch
        {
            { Variable: not null } start => $"starts as {start.Value}",
            null when DominoValueRefs.VariableOf(expr) is not null => "set while the graph runs",
            _ => null,
        };
        string text = string.Join("\n", new[] { origin, value is null ? null : Describe(value) }.OfType<string>());
        return text.Length == 0 ? null : text;
    }

    private string? Describe(ValueRef value) => value.Kind switch
    {
        ValueRefKind.Sound => SoundFile(value)?.Path ?? "not in the loaded game files",
        ValueRefKind.SoundType => int.TryParse(value.Value, CultureInfo.InvariantCulture, out int type)
            ? services.SoundTables.Value.SoundTypeName(type)
            : null,
        ValueRefKind.SoundMix => MixStart(value) is { } start ? $"starts sound 0x{start:x8}" : "plays no sound of its own",
        ValueRefKind.LocText => OasisStringTable.Resolve(value.Value) ?? "no localised text has this key",
        ValueRefKind.Entity => EntityFile(value) is { } entity ? EntityLabel(entity) : "not found in the loaded worlds",
        ValueRefKind.Graph => GraphFile(value.Value) is null ? "not in the loaded game files" : null,
        ValueRefKind.Texture => services.FindByPath(value.Value) is null ? "not in the loaded game files" : null,
        ValueRefKind.Bark or ValueRefKind.BarkBank => DescribeBark(value),
        _ => null,
    };

    public IReadOnlyList<DominoAction> For(ValueRef value)
    {
        var actions = new List<DominoAction>();
        switch (value.Kind)
        {
            case ValueRefKind.Sound when value.SoundId is { } id:
                actions.Add(PlaySound(value.Value, id));
                if (SoundFile(value) is { } file)
                {
                    actions.Add(ShowInFiles(file));
                    actions.Add(Copy("Copy path", file.Path));
                }
                break;
            case ValueRefKind.SoundMix when MixStart(value) is { } start:
                actions.Add(PlaySound(value.Value, start));
                break;
            case ValueRefKind.LocText when OasisStringTable.Resolve(value.Value) is { } text:
                actions.Add(Copy("Copy text", text));
                break;
            case ValueRefKind.Entity when EntityFile(value) is { } entity:
                actions.Add(new DominoAction("Open entity", () => services.OpenInEditor(entity)));
                break;
            case ValueRefKind.Graph when OpenGraphAction(value.Value) is { } open:
                actions.Add(open);
                break;
            case ValueRefKind.Texture when services.FindByPath(value.Value) is { } texture:
                actions.Add(ShowInFiles(texture));
                break;
            case ValueRefKind.Bark or ValueRefKind.BarkBank:
                AddBarkActions(actions, value);
                break;
        }

        if (value.Kind is ValueRefKind.Bark or ValueRefKind.BarkBank or ValueRefKind.Animation or ValueRefKind.Message)
        {
            actions.Add(new DominoAction("Select uses", () => selectUses(value)));
        }
        actions.Add(Copy("Copy", value.Value));
        return actions;
    }

    /// <summary>A small preview of a texture value, or null.</summary>
    public ImageSource? Thumbnail(ValueRef value)
    {
        if (value.Kind != ValueRefKind.Texture)
        {
            return null;
        }
        if (!_thumbnails.TryGetValue(value.Value, out ImageSource? image))
        {
            image = services.FindByPath(value.Value) is { } file ? XbtImage.TryDecode(services.Read(file), out _) : null;
            _thumbnails[value.Value] = image;
        }
        return image;
    }

    /// <summary>Opens the graph a sub-graph box or StartScript value names, when it is loaded.</summary>
    public DominoAction? OpenGraphAction(string nodeTypePath) =>
        GraphFile(nodeTypePath) is { } graph ? new DominoAction("Open graph", () => services.OpenGraph(graph)) : null;

    public static DominoAction Copy(string label, string text) => new(label, () => SetClipboard(text));

    private VfsFile? GraphFile(string nodeTypePath) => services.FindByPath(DominoNodeCatalog.ToVfsPath(nodeTypePath));

    private DominoAction ShowInFiles(VfsFile file) => new("Show in Files", () => services.ShowInFiles(file));

    // Decoding reads and parses whole banks, so it runs off the UI thread.
    private DominoAction PlaySound(string label, uint id) => new("▶ Play", () =>
        play(label, () => Task.Run(() => SoundPreview.SoundIdToTempWavAsync(id, services.ResolveSound, services.Read))));

    private (uint Bank, IReadOnlyList<BarkEntry> Barks)? BankOf(ValueRef value) =>
        services.Barks.Value is { IsCompletedSuccessfully: true } barks ? barks.Result.ForMission(value.Value) : null;

    /// <summary>The bank a tag loads and, for a PlayBark, who speaks the block's lines and how.</summary>
    private string DescribeBark(ValueRef value)
    {
        if (!services.Barks.Value.IsCompleted)
        {
            return "reading the bark banks…";
        }
        if (BankOf(value) is not var (bank, barks))
        {
            return "no bark bank answers to this mission tag";
        }

        string where = BarkBank.BankPath(bank);
        if (value.Kind == ValueRefKind.BarkBank)
        {
            return $"{where}  ·  {barks.Count} barks";
        }

        IReadOnlyList<BarkLine> lines = BarkBank.LinesOf(barks, value.Detail);
        return lines.Count == 0
            ? $"{where} has no {value.Detail} bark"
            : where + string.Concat(lines.Select((l, i) =>
                $"\n{i + 1}. {l.Speaker}" + (l.Emotion.Length > 0 ? $", {l.Emotion}" : "") + (l.Gesture.Length > 0 ? $", {l.Gesture}" : "")));
    }

    private void AddBarkActions(List<DominoAction> actions, ValueRef value)
    {
        if (BankOf(value) is not var (bank, barks))
        {
            return;
        }

        if (value.Kind == ValueRefKind.Bark)
        {
            IReadOnlyList<BarkLine> lines = BarkBank.LinesOf(barks, value.Detail);
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].SoundIds.Count > 0)
                {
                    actions.Add(PlayBarkLine($"▶ {i + 1}. {lines[i].Speaker}", $"{value.Value} {value.Detail} line {i + 1}", bank, lines[i].SoundIds[0]));
                }
            }
        }

        string path = BarkBank.BankPath(bank);
        if (services.FindByPath(path) is { } file)
        {
            actions.Add(ShowInFiles(file));
        }
        actions.Add(Copy("Copy bank path", path));
    }

    /// <summary>A line's sound ID is an event in the bank's own sound pack, not a file of its own.</summary>
    private DominoAction PlayBarkLine(string label, string title, uint bank, uint soundId) => new(label, () =>
        play(title, () => Task.Run(() =>
        {
            byte[] sounds = services.ReadBytes(BarkBank.SoundsPath(bank))
                ?? throw new InvalidOperationException($"{BarkBank.SoundsPath(bank)} isn't in the loaded game files.");
            return SoundPreview.SoundIdToTempWavAsync(soundId, services.ResolveSound, services.Read, SpkPackage.Parse(sounds));
        })));

    private VfsFile? SoundFile(ValueRef value) => value.SoundId is { } id ? services.ResolveSound(id) : null;

    private uint? MixStart(ValueRef value) => services.SoundTables.Value.MixStartSound(value.Value);

    private VfsFile? EntityFile(ValueRef value) => value.EntityId is { } id ? services.FindEntity(id) : null;

    /// <summary>A fragment row `…\worldsector12.data.fcb\Guard_12.2058514.xml` →
    /// `Guard_12  ·  worldsector12.data.fcb`.</summary>
    private static string EntityLabel(VfsFile entity)
    {
        string fragment = entity.FragmentId!;
        string leaf = Path.GetFileNameWithoutExtension(fragment);
        int dot = leaf.LastIndexOf('.');
        string name = dot > 0 ? leaf[..dot] : "(unnamed)";
        return $"{name}  ·  {Path.GetFileName(entity.Path[..^(fragment.Length + 1)])}";
    }

    private static void SetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Another process holds the clipboard; the user can simply click again.
        }
    }
}
