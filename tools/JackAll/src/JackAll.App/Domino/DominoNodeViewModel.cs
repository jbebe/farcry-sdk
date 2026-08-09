using System.Collections.ObjectModel;
using System.Windows;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.App.Domino;

/// <summary>What a node on the canvas represents.</summary>
public enum NodeRole
{
    /// <summary>A reconstructed box - one `self[N]`/`self.box_X` instance, or one pooled occurrence.</summary>
    Box,

    /// <summary>Not a box: this graph's own boundary, drawn so its interface is visible. A data input it
    /// receives from a parent graph, or a control-out pin it exposes.</summary>
    Boundary,
}

/// <summary>One node on the nodify canvas.</summary>
public sealed class DominoNodeViewModel : DominoObservable
{
    private Point _location;
    private bool _isSelected;

    public DominoNodeViewModel(GraphNode node, PositionedNode positioned)
    {
        Node = node;
        Role = NodeRole.Box;
        _location = new Point(positioned.X, positioned.Y);
        Width = positioned.Width;
        Title = node.DisplayName;
        Subtitle = BuildSubtitle(node);
        Category = node.Signature?.Category;
    }

    private DominoNodeViewModel(string title, string subtitle, Point location)
    {
        Node = null;
        Role = NodeRole.Boundary;
        _location = location;
        Width = 150;
        Title = title;
        Subtitle = subtitle;
        Category = null;
    }

    /// <summary>A boundary node standing for one of the graph's own data inputs - a variable the parent
    /// graph supplies. Its single output feeds every box that reads that variable.</summary>
    public static DominoNodeViewModel GraphInput(string variable, Point location)
    {
        var vm = new DominoNodeViewModel(variable, "graph input", location);
        vm.Output.Add(new DominoConnectorViewModel(variable, PortKind.Data) { Title = variable });
        return vm;
    }

    /// <summary>A boundary node standing for one of the graph's own control-out pins - what it fires
    /// when used as a sub-box by a parent.</summary>
    public static DominoNodeViewModel GraphExit(string pin, Point location)
    {
        var vm = new DominoNodeViewModel(pin, "graph output", location);
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

    public bool IsPooled => Node?.Kind == BoxInstanceKind.Pooled;
    public bool IsSubGraph => Node?.IsSubGraph == true;
    public bool IsBoundary => Role == NodeRole.Boundary;

    /// <summary>True when the node type's script couldn't be read, so there is no pin list - the node is
    /// drawn from whatever the graph itself referenced rather than from a signature.</summary>
    public bool SignatureMissing => Role == NodeRole.Box && Node?.Signature is null;

    /// <summary>The line under the title: what kind of box this is and where it lives in the script.</summary>
    private static string BuildSubtitle(GraphNode node)
    {
        string type = NodeSignature.ShortNameFor(node.NodeTypePath);
        return node.Ref switch
        {
            InstanceBoxRef i => $"{type}  ·  self[{i.Slot}]",
            NamedInstanceBoxRef => type,
            PooledBoxRef => $"{type}  ·  pooled, in {node.OwnerFunction}",
            _ => type,
        };
    }
}
