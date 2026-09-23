using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using JackAll.App.Audio;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>
/// The detail pane beside the canvas: what the selected box is, what it was configured with, and the
/// full pin interface its node type declares - including pins this graph never wired, which the canvas
/// shows as bare ports but doesn't explain.
///
/// Below that, the graph-level facts the model carries but no node owns: whether it matches its debug
/// twin, what the lint found, the node types `Create()` registers and the engine resources it loads
/// directly, bypassing the box system.
/// </summary>
public partial class DominoInspector : UserControl
{
    private sealed record ParamRow(string Name, string Value, string? Resolved, IReadOnlyList<DominoAction> Actions, ImageSource? Thumbnail)
    {
        public bool HasResolved => Resolved is not null;
    }

    private sealed record PinRow(string Direction, string Name, string Detail, string? Note)
    {
        public bool HasNote => Note is not null;
    }

    private sealed record ProblemRow(DominoFinding Finding)
    {
        public string Label => Finding.Severity switch
        {
            LintSeverity.Error => "error",
            LintSeverity.Warning => "warning",
            _ => "note",
        };

        public string Rule => Finding.Rule;
        public string Message => Finding.Message;
        public string? Where => Finding.Function;
        public bool HasWhere => Where is not null;
    }

    /// <summary>Raised when a problem is clicked, to go to what it is about.</summary>
    public event Action<DominoFinding>? ProblemActivated;

    private ReconstructedGraph? _graph;
    private DominoValueActions? _values;
    private string? _playing;
    private int _playRequest;

    public DominoInspector()
    {
        InitializeComponent();
        Unloaded += (_, _) => StopPlayer();
    }

    /// <summary>Fills in the graph-level sections, which don't change with selection.</summary>
    /// <param name="values">Resolves and acts on parameter values; null leaves them as plain text.</param>
    public void ShowGraph(ReconstructedGraph? graph, DominoDebugTwin? twin, string statusText, DominoValueActions? values,
        IReadOnlyList<DominoFinding> findings)
    {
        _graph = graph;
        _values = values;
        GraphSummary.Text = graph is null
            ? statusText
            : $"{graph.Nodes.Count} boxes, {graph.Edges.Count} control edges, {graph.DataEdges.Count} data edges"
              + (twin?.GraphName is { } name ? $"\n{name}" : "")
              + (twin?.DocumentPath is { } doc ? $"\n{doc}" : "")
              + (graph.Twin is { } check
                  ? check.IsClean
                      ? $"\nEvery one of the {check.TracedFires} fires its debug twin traces matches this graph."
                      : $"\nThe debug twin disagrees: {string.Join("; ", check.Problems.Take(3))}"
                  : "");

        var problems = ProblemRows(findings.OrderByDescending(f => f.Severity));
        GraphProblemList.ItemsSource = problems;
        GraphProblemsHeader.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (graph is not null && graph.RegisteredDependencies.Count > 0)
        {
            DepsHeader.Visibility = Visibility.Visible;
            DepList.ItemsSource = graph.RegisteredDependencies.Distinct().Order(StringComparer.Ordinal).ToList();
        }

        if (graph is not null && graph.LoadedResources.Count > 0)
        {
            ResourcesHeader.Visibility = Visibility.Visible;
            ResourceList.ItemsSource = graph.LoadedResources
                .Select(r => $"{r.Name}  ({r.Type})")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <param name="canvas">Supplies the boxes a boundary node feeds.</param>
    /// <param name="nodeActions">What can be done with the box as a whole.</param>
    /// <param name="refs">What the box's parameter values name.</param>
    public void ShowNode(DominoNodeViewModel? vm, DominoGraphViewModel? canvas, IReadOnlyList<DominoAction> nodeActions, IReadOnlyList<ValueRef> refs)
    {
        // The graph-level sections are the fallback view. Once a box is selected they'd just be noise
        // above the thing actually being inspected.
        GraphSection.Visibility = vm?.Node is null ? Visibility.Visible : Visibility.Collapsed;

        if (vm?.Node is not { } node)
        {
            Details.Visibility = Visibility.Collapsed;
            EmptyNotice.Visibility = Visibility.Visible;
            EmptyNotice.Text = vm is { IsBoundary: true }
                ? $"{vm.Tooltip}{ReadersText(vm, canvas)}"
                : "Select a box on the canvas to inspect it.";
            return;
        }

        EmptyNotice.Visibility = Visibility.Collapsed;
        Details.Visibility = Visibility.Visible;

        NodeTitle.Text = vm.Title;
        NodeCategory.Text = node.Signature?.Category ?? "uncategorized";
        NodeDescription.Text = node.Signature?.Doc?.Summary;
        NodeDescription.Visibility = NodeDescriptionSource.Visibility =
            node.Signature?.Doc is null ? Visibility.Collapsed : Visibility.Visible;
        NodeActions.Content = nodeActions;
        NodeTypePath.Text = node.NodeTypePath;
        NodeInstance.Text = IdSourceNote(node) is { } how ? $"{vm.Subtitle}\n{how}" : vm.Subtitle;

        var problems = ProblemRows(vm.Problems);
        NodeProblemList.ItemsSource = problems;
        NodeProblemsHeader.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        SignatureNotice.Visibility = node.Signature is null || node.Signature.Origin == SignatureOrigin.Inferred
            ? Visibility.Visible
            : Visibility.Collapsed;
        SignatureNotice.Text = node.Signature switch
        {
            null => "This node type's script couldn't be read, so its pin list is unknown — only pins this graph references are shown.",
            { Origin: SignatureOrigin.Inferred } => "Sub-graph: pins were recovered from its generated code, so control pins are exact but data pins are untyped and best-effort.",
            _ => string.Empty,
        };

        var parameters = BuildParamRows(node, refs);
        ParamList.ItemsSource = parameters;
        ParamsHeader.Visibility = parameters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var pins = BuildPinRows(node.Signature);
        PinList.ItemsSource = pins;
        PinsHeader.Visibility = pins.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<ParamRow> BuildParamRows(GraphNode node, IReadOnlyList<ValueRef> refs)
    {
        var rows = new List<ParamRow>();
        foreach ((string pin, var expr) in node.Params.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            ValueRef? value = refs.FirstOrDefault(r => r.Pin == pin);
            rows.Add(_values is null || _graph is null
                ? new ParamRow(pin, DominoExprPreview.Full(expr), null, [], null)
                : new ParamRow(pin, DominoExprPreview.Full(expr), _values.Explain(expr, value, _graph),
                    value is null ? [] : _values.For(value), value is null ? null : _values.Thumbnail(value)));
        }
        return rows;
    }

    private void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DominoAction action })
        {
            action.Run();
        }
    }

    /// <summary>Decodes a sound with <paramref name="makeWav"/> and plays it in the docked player. A
    /// newer request supersedes one still decoding.</summary>
    public async void Play(string label, Func<Task<string>> makeWav)
    {
        int request = ++_playRequest;
        StopPlayer();
        PlayerSection.Visibility = Visibility.Visible;
        PlayerLabel.Text = label;
        PlayerStatus.Text = "Decoding…";

        try
        {
            string wav = await makeWav();
            if (request != _playRequest)
            {
                SoundPreview.TryDelete(wav);
                return;
            }
            _playing = wav;
            PlayerStatus.Text = "";
            Player.Open(wav);
            Player.Play();
        }
        catch (Exception ex) when (request == _playRequest)
        {
            PlayerStatus.Text = ex.Message;
        }
    }

    private void StopPlayer()
    {
        Player.Reset();
        SoundPreview.TryDelete(_playing);
        _playing = null;
    }

    private static List<PinRow> BuildPinRows(NodeSignature? signature)
    {
        if (signature is null)
        {
            return [];
        }

        var rows = new List<PinRow>();
        rows.AddRange(signature.ControlIns.Select(p => new PinRow("in ▸", p.Name, p.Dynamic ? "dynamic" : "", signature.NoteFor(p.Name))));
        rows.AddRange(signature.ControlOuts.Select(p => new PinRow("out ▸", p.Name,
            string.Join(" ", new[] { p.Delayed ? "delayed" : null, p.Dynamic ? "dynamic" : null }.Where(s => s is not null)),
            signature.NoteFor(p.Name))));
        rows.AddRange(signature.DataIns.Select(p => new PinRow("in ●", p.Name, DominoTypes.Describe(p.Type), signature.NoteFor(p.Name))));
        rows.AddRange(signature.DataOuts.Select(p => new PinRow("out ●", p.Name, DominoTypes.Describe(p.Type), signature.NoteFor(p.Name))));
        return rows;
    }

    /// <summary>How a pooled box's editor ID was recovered, since the release code never states it.</summary>
    private static string? IdSourceNote(GraphNode node) => node.IdSource switch
    {
        BoxIdSource.Wire => "Pooled; the ID is the one its control-out handler is named after.",
        BoxIdSource.Prologue => "Pooled; the ID is the one its en_ prologue is named after.",
        BoxIdSource.Continuation => "Pooled; the ID is the one the continuation that fires it is named after.",
        BoxIdSource.Twin => "Pooled; nothing in the release code names it, the ID is the debug twin's.",
        BoxIdSource.None => "Pooled; nothing names it and there is no debug twin, so its editor ID is unknown.",
        _ => null,
    };

    private static List<ProblemRow> ProblemRows(IEnumerable<DominoFinding> findings) =>
        [.. findings.Select(f => new ProblemRow(f))];

    private void Problem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProblemRow row })
        {
            ProblemActivated?.Invoke(row.Finding);
        }
    }

    private static string ReadersText(DominoNodeViewModel boundary, DominoGraphViewModel? canvas)
    {
        var readers = canvas?.ReadersOf(boundary).Select(r => $"• {r.Title}  ({r.Subtitle})").ToList() ?? [];
        return readers.Count == 0 ? "" : $"\n\nRead by:\n{string.Join("\n", readers)}";
    }
}
