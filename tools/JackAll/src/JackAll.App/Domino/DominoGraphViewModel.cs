using System.Collections.ObjectModel;
using System.Windows;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.App.Domino;

/// <summary>
/// Turns a <see cref="ReconstructedGraph"/> into what the canvas binds to: a node per box, a port per
/// pin, and a wire per connection.
///
/// Ports come from the node type's <see cref="NodeSignature"/> so a box shows its whole interface -
/// including pins nothing is wired to, which is information (that pin exists and is unused), not noise.
/// Where the graph references a pin the signature doesn't declare - a node type this install can't read,
/// or a sub-graph pin inference missed - the port is created on demand and flagged undeclared, so the
/// wire still lands somewhere visible instead of silently disappearing.
///
/// The graph's own boundary gets nodes too: one per data input it takes from a parent graph, one per
/// control-out pin it exposes. Without them a sub-graph's interface is invisible, and nodify has no
/// second anchor to draw those connections against.
/// </summary>
public sealed class DominoGraphViewModel : DominoObservable
{
    private readonly Dictionary<string, DominoNodeViewModel> _byNodeId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DominoNodeViewModel> _graphInputs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DominoNodeViewModel> _graphExits = new(StringComparer.Ordinal);
    private DominoNodeViewModel? _selectedNode;

    public ObservableCollection<DominoNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<DominoConnectionViewModel> Connections { get; } = [];

    /// <summary>What the inspector is showing. Two-way with the canvas's own selection.</summary>
    public DominoNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set => Set(ref _selectedNode, value);
    }

    public int ControlWireCount { get; private set; }
    public int DataWireCount { get; private set; }
    public int UnwiredPinCount { get; private set; }
    public int DeadEndPinCount { get; private set; }
    public int AmbiguousDataWireCount { get; private set; }

    public DominoGraphViewModel(ReconstructedGraph graph, IReadOnlyList<PositionedNode> layout, DominoDebugTwin? twin)
    {
        IReadOnlyDictionary<(string Box, string Pin), string> labels = BuildPinLabels(twin);

        foreach (PositionedNode positioned in layout)
        {
            var vm = new DominoNodeViewModel(positioned.Node, positioned);
            AddDeclaredPorts(vm, positioned.Node, labels);
            _byNodeId[positioned.Node.Id] = vm;
            Nodes.Add(vm);
        }

        // Boundary nodes are placed just outside the laid-out graph on the side they belong to.
        double minX = layout.Count > 0 ? layout.Min(p => p.X) : 0;
        double maxX = layout.Count > 0 ? layout.Max(p => p.X + p.Width) : 0;
        double midY = layout.Count > 0 ? layout.Average(p => p.Y) : 0;

        AddControlWires(graph, labels, maxX, midY);
        AddDataWires(graph, minX, midY);
        MarkConnectedPorts();
    }

    // ------------------------------------------------------------------ ports

    private static void AddDeclaredPorts(
        DominoNodeViewModel vm,
        GraphNode node,
        IReadOnlyDictionary<(string, string), string> labels)
    {
        NodeSignature? signature = node.Signature;
        if (signature is null)
        {
            return; // ports get created on demand from whatever the graph actually references
        }

        foreach (ControlInPin pin in signature.ControlIns)
        {
            vm.Input.Add(new DominoConnectorViewModel(pin.Name, PortKind.Control)
            {
                Title = Label(labels, node, pin.Name),
            });
        }
        foreach (DataInPin pin in signature.DataIns)
        {
            vm.Input.Add(new DominoConnectorViewModel(pin.Name, PortKind.Data, pin.Type)
            {
                Title = pin.Name,
            });
        }
        foreach (ControlOutPin pin in signature.ControlOuts)
        {
            vm.Output.Add(new DominoConnectorViewModel(pin.Name, PortKind.Control, type: null, pin.Delayed)
            {
                Title = Label(labels, node, pin.Name),
            });
        }
        foreach (DataOutPin pin in signature.DataOuts)
        {
            vm.Output.Add(new DominoConnectorViewModel(pin.Name, PortKind.Data, pin.Type)
            {
                Title = pin.Name,
            });
        }
    }

    /// <summary>Finds a port by pin name, creating an undeclared one if the signature didn't list it.</summary>
    private static DominoConnectorViewModel Port(
        ObservableCollection<DominoConnectorViewModel> ports,
        string name,
        PortKind kind)
    {
        foreach (DominoConnectorViewModel existing in ports)
        {
            if (existing.Kind == kind && string.Equals(existing.Name, name, StringComparison.Ordinal))
            {
                return existing;
            }
        }

        var created = new DominoConnectorViewModel(name, kind, type: null, delayed: false, declared: false) { Title = name };
        ports.Add(created);
        return created;
    }

    // ------------------------------------------------------------------ wires

    private void AddControlWires(
        ReconstructedGraph graph,
        IReadOnlyDictionary<(string, string), string> labels,
        double exitX,
        double midY)
    {
        foreach (GraphEdge edge in graph.Edges)
        {
            if (!_byNodeId.TryGetValue(edge.SourceNodeId, out DominoNodeViewModel? source))
            {
                continue;
            }

            switch (edge.Target)
            {
                case EdgeTarget.Node when edge.TargetNodeId is not null && edge.TargetPin is not null
                                       && _byNodeId.TryGetValue(edge.TargetNodeId, out DominoNodeViewModel? target):
                    Connections.Add(new DominoConnectionViewModel
                    {
                        Source = Port(source.Output, edge.SourcePin, PortKind.Control),
                        Target = Port(target.Input, edge.TargetPin, PortKind.Control),
                        Kind = PortKind.Control,
                    });
                    ControlWireCount++;
                    break;

                case EdgeTarget.GraphExit when edge.GraphExitPin is not null:
                    DominoNodeViewModel exit = BoundaryExit(edge.GraphExitPin, exitX, midY);
                    Connections.Add(new DominoConnectionViewModel
                    {
                        Source = Port(source.Output, edge.SourcePin, PortKind.Control),
                        Target = exit.Input[0],
                        Kind = PortKind.Control,
                    });
                    ControlWireCount++;
                    break;

                case EdgeTarget.Unwired:
                    // The pin exists but was never connected in the editor. Make sure it's still drawn.
                    Port(source.Output, edge.SourcePin, PortKind.Control);
                    UnwiredPinCount++;
                    break;

                case EdgeTarget.DeadEnd:
                    Port(source.Output, edge.SourcePin, PortKind.Control);
                    DeadEndPinCount++;
                    break;
            }
        }
    }

    private void AddDataWires(ReconstructedGraph graph, double inputX, double midY)
    {
        foreach (DataEdge edge in graph.DataEdges)
        {
            if (!_byNodeId.TryGetValue(edge.TargetNodeId, out DominoNodeViewModel? target))
            {
                continue;
            }

            DominoConnectorViewModel? sourcePort = edge.Kind switch
            {
                DataEdgeKind.GraphInput when edge.ViaVariable is not null =>
                    BoundaryInput(edge.ViaVariable, inputX, midY).Output[0],
                DataEdgeKind.NodeToNode when edge.SourceNodeId is not null && edge.SourcePin is not null
                                          && _byNodeId.TryGetValue(edge.SourceNodeId, out DominoNodeViewModel? source) =>
                    Port(source.Output, edge.SourcePin, PortKind.Data),
                _ => null,
            };

            if (sourcePort is null)
            {
                continue;
            }

            Connections.Add(new DominoConnectionViewModel
            {
                Source = sourcePort,
                Target = Port(target.Input, edge.TargetPin, PortKind.Data),
                Kind = PortKind.Data,
                Ambiguous = edge.Ambiguous,
                Label = edge.ViaVariable,
                SourceOccurrences = edge.SourceOccurrences,
            });
            DataWireCount++;
            if (edge.Ambiguous)
            {
                AmbiguousDataWireCount++;
            }
        }
    }

    private DominoNodeViewModel BoundaryInput(string variable, double x, double midY)
    {
        if (_graphInputs.TryGetValue(variable, out DominoNodeViewModel? existing))
        {
            return existing;
        }

        var vm = DominoNodeViewModel.GraphInput(variable, new Point(x - 260, midY + (_graphInputs.Count * 70)));
        _graphInputs[variable] = vm;
        Nodes.Add(vm);
        return vm;
    }

    private DominoNodeViewModel BoundaryExit(string pin, double x, double midY)
    {
        if (_graphExits.TryGetValue(pin, out DominoNodeViewModel? existing))
        {
            return existing;
        }

        var vm = DominoNodeViewModel.GraphExit(pin, new Point(x + 110, midY + (_graphExits.Count * 70)));
        _graphExits[pin] = vm;
        Nodes.Add(vm);
        return vm;
    }

    private void MarkConnectedPorts()
    {
        foreach (DominoConnectionViewModel connection in Connections)
        {
            connection.Source.IsConnected = true;
            connection.Target.IsConnected = true;
        }
    }

    // ------------------------------------------------------------------ labels

    /// <summary>The editor's own pin labels, keyed by box name and the mangled identifier the generated
    /// Lua uses - `("box_WAGER_v3_47", "_4a__Wager_finished__Buddy_healthy")` maps back to
    /// `"4a. Wager finished, Buddy healthy"`.</summary>
    private static IReadOnlyDictionary<(string Box, string Pin), string> BuildPinLabels(DominoDebugTwin? twin)
    {
        var labels = new Dictionary<(string, string), string>();
        if (twin is null)
        {
            return labels;
        }

        foreach (TracedConnection c in twin.Connections)
        {
            if (c.SourceBox is not null)
            {
                labels[(c.SourceBox, DominoDebugTwin.ToIdentifier(c.SourcePinLabel))] = c.SourcePinLabel;
            }
            if (c.TargetBox is not null)
            {
                labels[(c.TargetBox, DominoDebugTwin.ToIdentifier(c.TargetPinLabel))] = c.TargetPinLabel;
            }
        }
        return labels;
    }

    private static string Label(IReadOnlyDictionary<(string, string), string> labels, GraphNode node, string pin) =>
        node.OriginalName is { } box && labels.TryGetValue((box, pin), out string? label) ? label : pin;
}
