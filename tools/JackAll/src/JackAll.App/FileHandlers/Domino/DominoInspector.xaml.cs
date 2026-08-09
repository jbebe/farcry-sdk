using System.Windows;
using System.Windows.Controls;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>
/// The detail pane beside the canvas: what the selected box is, what it was configured with, and the
/// full pin interface its node type declares - including pins this graph never wired, which the canvas
/// shows as bare ports but doesn't explain.
///
/// Below that, the graph-level facts the model carries but no node owns: the node types `Create()`
/// registers and the engine resources it loads directly, bypassing the box system.
/// </summary>
public partial class DominoInspector : UserControl
{
    private sealed record ParamRow(string Name, string Value);

    private sealed record PinRow(string Direction, string Name, string Detail);

    public DominoInspector() => InitializeComponent();

    /// <summary>Fills in the graph-level sections, which don't change with selection.</summary>
    public void ShowGraph(ReconstructedGraph? graph, DominoDebugTwin? twin, string statusText)
    {
        GraphSummary.Text = graph is null
            ? statusText
            : $"{graph.Nodes.Count} boxes, {graph.Edges.Count} control edges, {graph.DataEdges.Count} data edges"
              + (twin?.GraphName is { } name ? $"\n{name}" : "")
              + (twin?.DocumentPath is { } doc ? $"\n{doc}" : "");

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

    public void ShowNode(DominoNodeViewModel? vm)
    {
        // The graph-level sections are the fallback view. Once a box is selected they'd just be noise
        // above the thing actually being inspected.
        GraphSection.Visibility = vm?.Node is null ? Visibility.Visible : Visibility.Collapsed;

        if (vm?.Node is not { } node)
        {
            Details.Visibility = Visibility.Collapsed;
            EmptyNotice.Visibility = Visibility.Visible;
            EmptyNotice.Text = vm is { IsBoundary: true }
                ? $"“{vm.Title}” is this graph's own {vm.Subtitle}, not a box."
                : "Select a box on the canvas to inspect it.";
            return;
        }

        EmptyNotice.Visibility = Visibility.Collapsed;
        Details.Visibility = Visibility.Visible;

        NodeTitle.Text = node.DisplayName;
        NodeCategory.Text = node.Signature?.Category ?? "uncategorized";
        NodeTypePath.Text = node.NodeTypePath;
        NodeInstance.Text = vm.Subtitle;

        SignatureNotice.Visibility = node.Signature is null || node.Signature.Origin == SignatureOrigin.Inferred
            ? Visibility.Visible
            : Visibility.Collapsed;
        SignatureNotice.Text = node.Signature switch
        {
            null => "This node type's script couldn't be read, so its pin list is unknown — only pins this graph references are shown.",
            { Origin: SignatureOrigin.Inferred } => "Sub-graph: pins were recovered from its generated code, so control pins are exact but data pins are untyped and best-effort.",
            _ => string.Empty,
        };

        var parameters = node.Params
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new ParamRow(p.Key, DominoExprPreview.Full(p.Value)))
            .ToList();
        ParamList.ItemsSource = parameters;
        ParamsHeader.Visibility = parameters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var pins = BuildPinRows(node.Signature);
        PinList.ItemsSource = pins;
        PinsHeader.Visibility = pins.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static List<PinRow> BuildPinRows(NodeSignature? signature)
    {
        if (signature is null)
        {
            return [];
        }

        var rows = new List<PinRow>();
        rows.AddRange(signature.ControlIns.Select(p => new PinRow("in ▸", p.Name, p.Dynamic ? "dynamic" : "")));
        rows.AddRange(signature.ControlOuts.Select(p => new PinRow("out ▸", p.Name,
            string.Join(" ", new[] { p.Delayed ? "delayed" : null, p.Dynamic ? "dynamic" : null }.Where(s => s is not null)))));
        rows.AddRange(signature.DataIns.Select(p => new PinRow("in ●", p.Name, p.Type)));
        rows.AddRange(signature.DataOuts.Select(p => new PinRow("out ●", p.Name, p.Type)));
        return rows;
    }
}
