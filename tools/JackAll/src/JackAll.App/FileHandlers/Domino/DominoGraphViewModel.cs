using System.Collections.ObjectModel;
using System.Windows;
using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;

namespace JackAll.App.FileHandlers.Domino;

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
    private readonly Dictionary<string, List<string>> _neighbours = new(StringComparer.Ordinal);
    private DominoNodeViewModel? _selectedNode;
    private int _focusHops;

    public ObservableCollection<DominoNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<DominoConnectionViewModel> Connections { get; } = [];

    /// <summary>What the inspector is showing. Two-way with the canvas's own selection.</summary>
    public DominoNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (Set(ref _selectedNode, value))
            {
                ApplyFocus();
            }
        }
    }

    /// <summary>How many hops from the selected node stay in focus; 0 turns focus mode off and shows
    /// everything. A 752-box graph is not navigable whole, so this is the difference between reading a
    /// mission's logic and staring at a wall.</summary>
    public int FocusHops
    {
        get => _focusHops;
        set
        {
            if (Set(ref _focusHops, value))
            {
                ApplyFocus();
            }
        }
    }

    /// <summary>How many nodes are currently in focus, for the status line.</summary>
    public int NodesInFocus { get; private set; }

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
        BuildAdjacency(graph);
        NodesInFocus = Nodes.Count;
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
                        SourceNode = source,
                        TargetNode = target,
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
                        SourceNode = source,
                        TargetNode = exit,
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
        HashSet<(string NodeId, string Pin)> hubs = DataHubs.Find(graph);
        var fanOut = graph.DataEdges
            .Where(e => e.SourceNodeId is not null && e.SourcePin is not null)
            .GroupBy(e => (NodeId: e.SourceNodeId!, Pin: e.SourcePin!))
            .ToDictionary(g => g.Key, g => g.Select(e => e.TargetNodeId).Distinct().Count());

        foreach (DataEdge edge in graph.DataEdges)
        {
            if (!_byNodeId.TryGetValue(edge.TargetNodeId, out DominoNodeViewModel? target))
            {
                continue;
            }

            // A hub source is shown as a chip on each consumer naming the variable the value travels
            // through, instead of a wire crossing the whole canvas to reach it.
            if (edge.IsHub(hubs) && edge.ViaVariable is not null)
            {
                AddSupplierChip(target, edge, fanOut);
                continue;
            }

            DominoNodeViewModel? sourceNode = edge.Kind switch
            {
                DataEdgeKind.GraphInput when edge.ViaVariable is not null => BoundaryInput(edge.ViaVariable, inputX, midY),
                DataEdgeKind.NodeToNode when edge.SourceNodeId is not null && edge.SourcePin is not null
                    => _byNodeId.GetValueOrDefault(edge.SourceNodeId),
                _ => null,
            };

            if (sourceNode is null)
            {
                continue;
            }

            DominoConnectorViewModel sourcePort = edge.Kind == DataEdgeKind.GraphInput
                ? sourceNode.Output[0]
                : Port(sourceNode.Output, edge.SourcePin!, PortKind.Data);

            Connections.Add(new DominoConnectionViewModel
            {
                Source = sourcePort,
                Target = Port(target.Input, edge.TargetPin, PortKind.Data),
                Kind = PortKind.Data,
                SourceNode = sourceNode,
                TargetNode = target,
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

    /// <summary>Replaces one hub wire with a chip on the consumer port, and records the fan-out on the
    /// producer port so the source still says how far it reaches.</summary>
    private void AddSupplierChip(
        DominoNodeViewModel target,
        DataEdge edge,
        Dictionary<(string NodeId, string Pin), int> fanOut)
    {
        Port(target.Input, edge.TargetPin, PortKind.Data).SupplyFrom(edge.ViaVariable!, edge.SourceNodeId);
        ChipCount++;

        if (edge.SourceNodeId is not null
            && edge.SourcePin is not null
            && _byNodeId.TryGetValue(edge.SourceNodeId, out DominoNodeViewModel? source))
        {
            Port(source.Output, edge.SourcePin, PortKind.Data)
                .MarkAsHub(fanOut.GetValueOrDefault((edge.SourceNodeId, edge.SourcePin)));
        }
    }

    /// <summary>How many hub wires were replaced by chips, for the status line.</summary>
    public int ChipCount { get; private set; }

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

    // ------------------------------------------------------------------ focus

    /// <summary>
    /// Undirected adjacency for focus traversal. Deliberately includes the hub connections that are
    /// drawn as chips rather than wires: suppressing a wire is a rendering decision, and a box you can
    /// reach through `self.Player` is still a neighbour when you ask what a node is connected to.
    /// </summary>
    private void BuildAdjacency(ReconstructedGraph graph)
    {
        void Link(string a, string b)
        {
            if (!_neighbours.TryGetValue(a, out List<string>? forward))
            {
                _neighbours[a] = forward = [];
            }
            forward.Add(b);

            if (!_neighbours.TryGetValue(b, out List<string>? backward))
            {
                _neighbours[b] = backward = [];
            }
            backward.Add(a);
        }

        foreach (DominoConnectionViewModel connection in Connections)
        {
            Link(connection.SourceNode.Key, connection.TargetNode.Key);
        }

        foreach (DataEdge edge in graph.DataEdges)
        {
            if (edge.SourceNodeId is not null
                && _byNodeId.ContainsKey(edge.SourceNodeId)
                && _byNodeId.ContainsKey(edge.TargetNodeId))
            {
                Link(edge.SourceNodeId, edge.TargetNodeId);
            }
        }
    }

    /// <summary>Fades everything more than <see cref="FocusHops"/> steps from the selection. With focus
    /// off, or nothing selected, everything is shown.</summary>
    private void ApplyFocus()
    {
        if (_focusHops <= 0 || _selectedNode is null)
        {
            foreach (DominoNodeViewModel node in Nodes)
            {
                node.IsFaded = false;
            }
            foreach (DominoConnectionViewModel connection in Connections)
            {
                connection.IsFaded = false;
            }
            NodesInFocus = Nodes.Count;
            OnPropertyChanged(nameof(NodesInFocus));
            return;
        }

        var inFocus = new HashSet<string>(StringComparer.Ordinal) { _selectedNode.Key };
        var frontier = new List<string> { _selectedNode.Key };

        for (int hop = 0; hop < _focusHops && frontier.Count > 0; hop++)
        {
            var next = new List<string>();
            foreach (string key in frontier)
            {
                foreach (string neighbour in _neighbours.TryGetValue(key, out List<string>? list) ? list : [])
                {
                    if (inFocus.Add(neighbour))
                    {
                        next.Add(neighbour);
                    }
                }
            }
            frontier = next;
        }

        foreach (DominoNodeViewModel node in Nodes)
        {
            node.IsFaded = !inFocus.Contains(node.Key);
        }
        foreach (DominoConnectionViewModel connection in Connections)
        {
            connection.IsFaded = !inFocus.Contains(connection.SourceNode.Key) || !inFocus.Contains(connection.TargetNode.Key);
        }

        NodesInFocus = inFocus.Count;
        OnPropertyChanged(nameof(NodesInFocus));
    }

    /// <summary>Selects the node a supplier chip stands for, so a suppressed hub wire is still one
    /// click from its source.</summary>
    public void SelectSupplier(DominoConnectorViewModel chip)
    {
        if (chip.SupplierNodeId is not null && _byNodeId.TryGetValue(chip.SupplierNodeId, out DominoNodeViewModel? supplier))
        {
            SelectedNode = supplier;
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
