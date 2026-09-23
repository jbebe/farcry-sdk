using System.Collections.ObjectModel;
using System.Windows;
using JackAll.Tools.Domino;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>What a node on the canvas represents.</summary>
public enum NodeRole
{
    /// <summary>A reconstructed editor box, persistent or pooled.</summary>
    Box,

    /// <summary>Not a box: this graph's own boundary, drawn so its interface is visible. A data input it
    /// receives from a parent graph, or a control pin it exposes.</summary>
    Boundary,
}

/// <summary>One node on the nodify canvas.</summary>
public sealed class DominoNodeViewModel : Observable
{
    private Point _location;
    private bool _isSelected;
    private bool _isFaded;

    public DominoNodeViewModel(GraphNode node, PositionedNode positioned)
    {
        Node = node;
        Role = NodeRole.Box;
        _location = new Point(positioned.X, positioned.Y);
        Width = positioned.Width;
        Title = node.TypeTitle;
        Subtitle = node.InstanceLabel;
        Category = node.Signature?.Category;
        Tooltip = node.Signature?.Doc is { } doc ? $"{Title}\n{doc.Summary}" : $"{Title}\n{node.NodeTypePath}";
    }

    private DominoNodeViewModel(string title, string subtitle, string tooltip)
    {
        Node = null;
        Role = NodeRole.Boundary;
        // Measured like any other node: a graph input named PrimaryBuddy_Entity does not fit in a
        // fixed-width box, and an overflowing port drags its wire's anchor outside the node.
        Width = Math.Clamp(Math.Max(TextMetrics.Width(title, 11) + 70, TextMetrics.Width(subtitle, 9) + 30), 150, 320);
        Title = title;
        Subtitle = subtitle;
        Tooltip = tooltip;
        Category = null;
    }

    /// <summary>A boundary node standing for a graph variable boxes read but no box writes. Its single
    /// output feeds every box that reads it. <paramref name="type"/> is the declared type of the pins
    /// reading it, when they agree; <paramref name="initValue"/> is what `Init()` sets it to.</summary>
    public static DominoNodeViewModel GraphInput(string variable, string? type, string? initValue)
    {
        string kind = DominoTypes.Describe(type);
        var vm = initValue is null
            ? new DominoNodeViewModel(variable, $"graph input  ·  {kind}",
                $"self.{variable} ({kind}): nothing in this graph sets it, so a parent graph using this one as a box supplies it.")
            : new DominoNodeViewModel(variable, $"graph variable  ·  {kind} = {initValue}",
                $"self.{variable} ({kind}): set to {initValue} when the graph starts (in Init), then read by the boxes wired to it.");
        vm.Output.Add(new DominoConnectorViewModel(variable, PortKind.Data, type) { Title = variable });
        return vm;
    }

    /// <summary>A boundary node standing for one of the graph's own control-in pins - what a parent
    /// graph, or the engine for a mission's top graph, fires to start the chain behind it.</summary>
    public static DominoNodeViewModel GraphEntry(string pin)
    {
        var vm = new DominoNodeViewModel(pin, "graph entry pin",
            $"{pin}: an input this graph exposes. A parent graph using this one as a box fires it; what is wired from here runs.");
        vm.Output.Add(new DominoConnectorViewModel(pin, PortKind.Control) { Title = pin });
        return vm;
    }

    /// <summary>A boundary node standing for one of the graph's own control-out pins - what it fires
    /// when used as a sub-box by a parent.</summary>
    public static DominoNodeViewModel GraphExit(string pin)
    {
        var vm = new DominoNodeViewModel(pin, "graph output",
            $"{pin}: an output this graph fires. A parent graph using this one as a box can wire it onward.");
        vm.Input.Add(new DominoConnectorViewModel(pin, PortKind.Control) { Title = pin });
        return vm;
    }

    /// <summary>The reconstructed box, or null for a <see cref="NodeRole.Boundary"/> node.</summary>
    public GraphNode? Node { get; }

    public NodeRole Role { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public string? Category { get; }
    public double Width { get; }

    /// <summary>What the box does, or what the boundary node stands for.</summary>
    public string Tooltip { get; }

    /// <summary>The size nodify rendered this node at, written back by its container.</summary>
    public Size ActualSize { get; set; }

    public ObservableCollection<DominoConnectorViewModel> Input { get; } = [];
    public ObservableCollection<DominoConnectorViewModel> Output { get; } = [];

    public Point Location
    {
        get => _location;
        set => Set(ref _location, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    /// <summary>Identifies this node for focus-mode traversal. Boundary nodes have no reconstructed box
    /// behind them, so they key off their title instead.</summary>
    public string Key => Node?.Id ?? $"boundary:{Subtitle}:{Title}";

    /// <summary>Outside the focus radius: still drawn, but pushed back so the neighbourhood in focus
    /// reads clearly. Hiding outright would make the graph jump around as the selection moves.</summary>
    public bool IsFaded
    {
        get => _isFaded;
        set
        {
            if (Set(ref _isFaded, value))
            {
                OnPropertyChanged(nameof(Opacity));
            }
        }
    }

    public double Opacity => _isFaded ? 0.1 : 1.0;

    public bool IsPooled => Node?.Kind == BoxInstanceKind.Pooled;
    public bool IsSubGraph => Node?.IsSubGraph == true;
    public bool IsBoundary => Role == NodeRole.Boundary;

    /// <summary>True when the node type's script couldn't be read, so there is no pin list - the node is
    /// drawn from whatever the graph itself referenced rather than from a signature.</summary>
    public bool SignatureMissing => Role == NodeRole.Box && Node?.Signature is null;

    /// <summary>What the lint found wrong with this box.</summary>
    public IReadOnlyList<DominoFinding> Problems { get; set; } = [];

    public bool HasErrors => Problems.Any(p => p.Severity == LintSeverity.Error);
}
