using JackAll.Tools.Domino.Graphs;

namespace JackAll.App.Domino;

/// <summary>One node positioned for display, sized to fit the ports it actually has.</summary>
public sealed record PositionedNode(GraphNode Node, double X, double Y, double Width, double Height);

/// <summary>
/// Layered ("Sugiyama") graph layout, run once per connected component.
///
/// The per-component part matters as much as the layering. A big mission graph is not one graph:
/// `a1bu00_storymission` is 23 disconnected islands - one blob of 582 boxes, three of exactly 46 (the
/// four repeated story branches), and 14 lone boxes. Laying the whole node set out in one shared set of
/// columns interleaves those islands, stacking unrelated logic on top of each other for no reason. Each
/// component gets its own band instead, largest first, with the lone boxes packed into a grid at the
/// bottom rather than each claiming a band of its own.
///
/// Within a component: longest-path layering for columns, then median-heuristic sweeps to cut edge
/// crossings, then coordinates. Cycles are real in mission logic - a gameplay loop wiring back to an
/// earlier box - so back-edges are found and removed before layering, which would otherwise not
/// terminate.
///
/// High fan-out data sources are excluded from the constraint set, matching the fact that they are not
/// drawn as wires either (see <see cref="DataHubs"/>); letting one box that 41 others read pull all 41
/// into alignment with it is precisely the distortion that makes these graphs unreadable.
/// </summary>
public static class SugiyamaLayout
{
    private const double ColumnGap = 110;
    private const double RowGap = 28;
    private const double HeaderHeight = 34;
    private const double PortHeight = 17;
    private const double MinNodeHeight = 56;

    // Node width is measured from the port names a node actually has - see WidthOf. These are the
    // fixed costs around the text: the connector dot and its spacing, the port row's border padding,
    // the gap between the input and output columns, and the node's own chrome.
    private const double NodeMinWidth = 210;

    /// <summary>Past this, a pathological name is left to ellipsize rather than stretching the node
    /// (and its whole column) far enough to hurt the layout more than the truncation does.</summary>
    private const double NodeMaxWidth = 460;

    private const double PortFontSize = 11;
    private const double ChipFontSize = 10;
    private const double ConnectorWidth = 22;
    private const double PortRowPadding = 14;
    private const double PortColumnGap = 18;
    private const double ChipPadding = 16;
    private const double HeaderPadding = 22;

    /// <summary>Vertical space between one component's band and the next.</summary>
    private const double BandGap = 140;

    /// <summary>Lone boxes are packed into rows this wide instead of getting a band each.</summary>
    private const int SingletonsPerRow = 10;

    /// <summary>How many up-and-down ordering sweeps to run. Crossing counts fall off fast and are
    /// essentially settled by four; more just costs time on the 232-box graphs. Measured on the corpus:
    /// four sweeps take `a1bu00_storymission` from 37,141 wire crossings to 20,228, and
    /// `a1lm01_copkiller` from 175 to 115.</summary>
    private const int OrderingSweeps = 4;

    public static IReadOnlyList<PositionedNode> Layout(ReconstructedGraph graph)
    {
        if (graph.Nodes.Count == 0)
        {
            return [];
        }

        var index = graph.Nodes.Select((node, i) => (node, i)).ToDictionary(t => t.node.Id, t => t.i, StringComparer.Ordinal);
        List<(int From, int To)> edges = CollectEdges(graph, index);
        Dictionary<string, double> widths = ComputeWidths(graph);

        List<List<int>> components = FindComponents(graph.Nodes.Count, edges);

        // Largest first, so the graph's main body is at the top rather than buried under fragments.
        var bands = components.Where(c => c.Count > 1).OrderByDescending(c => c.Count).ToList();
        var singletons = components.Where(c => c.Count == 1).Select(c => c[0]).ToList();

        var positioned = new List<PositionedNode>(graph.Nodes.Count);
        double bandTop = 0;

        foreach (List<int> component in bands)
        {
            List<PositionedNode> laid = LayoutComponent(graph, component, edges, widths);
            foreach (PositionedNode p in laid)
            {
                positioned.Add(p with { Y = p.Y + bandTop });
            }
            bandTop += laid.Max(p => p.Y + p.Height) + BandGap;
        }

        positioned.AddRange(PackSingletons(graph, singletons, bandTop, widths));
        return positioned;
    }

    /// <summary>
    /// Sizes every node to the widest thing it has to show. A port row is a connector dot, an optional
    /// chip naming the variable a hub-fed value arrives through, and the pin's own name; the node has to
    /// fit its widest input row beside its widest output row, or the connectors get pushed outside the
    /// node body and every wire anchored to them is dragged off with them.
    ///
    /// Port names come from the node type's signature where there is one, plus any pin the graph
    /// actually references - the same union the view model builds ports from, so the measured width
    /// matches what gets rendered.
    /// </summary>
    private static Dictionary<string, double> ComputeWidths(ReconstructedGraph graph)
    {
        var inputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var outputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var chips = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        HashSet<string> Bucket(Dictionary<string, HashSet<string>> map, string nodeId) =>
            map.TryGetValue(nodeId, out HashSet<string>? set) ? set : map[nodeId] = new HashSet<string>(StringComparer.Ordinal);

        foreach (GraphNode node in graph.Nodes)
        {
            HashSet<string> ins = Bucket(inputs, node.Id);
            HashSet<string> outs = Bucket(outputs, node.Id);
            if (node.Signature is { } signature)
            {
                foreach (var pin in signature.ControlIns) ins.Add(pin.Name);
                foreach (var pin in signature.DataIns) ins.Add(pin.Name);
                foreach (var pin in signature.ControlOuts) outs.Add(pin.Name);
                foreach (var pin in signature.DataOuts) outs.Add(pin.Name);
            }
        }

        foreach (GraphEdge edge in graph.Edges)
        {
            Bucket(outputs, edge.SourceNodeId).Add(edge.SourcePin);
            if (edge.Target == EdgeTarget.Node && edge.TargetNodeId is not null && edge.TargetPin is not null)
            {
                Bucket(inputs, edge.TargetNodeId).Add(edge.TargetPin);
            }
        }

        HashSet<(string, string)> hubs = DataHubs.Find(graph);
        foreach (DataEdge edge in graph.DataEdges)
        {
            Bucket(inputs, edge.TargetNodeId).Add(edge.TargetPin);
            if (edge.SourceNodeId is not null && edge.SourcePin is not null)
            {
                Bucket(outputs, edge.SourceNodeId).Add(edge.SourcePin);
            }
            if (edge.IsHub(hubs) && edge.ViaVariable is not null)
            {
                Bucket(chips, edge.TargetNodeId).Add($"self.{edge.ViaVariable}");
            }
        }

        var widths = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (GraphNode node in graph.Nodes)
        {
            double chipWidth = chips.TryGetValue(node.Id, out HashSet<string>? c) && c.Count > 0
                ? c.Max(t => TextMetrics.Width(t, ChipFontSize)) + ChipPadding
                : 0;

            double inputWidth = inputs[node.Id].Count == 0
                ? 0
                : inputs[node.Id].Max(n => TextMetrics.Width(n, PortFontSize)) + chipWidth + ConnectorWidth + PortRowPadding;

            double outputWidth = outputs[node.Id].Count == 0
                ? 0
                : outputs[node.Id].Max(n => TextMetrics.Width(n, PortFontSize)) + ConnectorWidth + PortRowPadding;

            double headerWidth = Math.Max(
                TextMetrics.Width(node.DisplayName, 12, bold: true),
                TextMetrics.Width(node.NodeTypePath, 9)) + HeaderPadding;

            double content = Math.Max(headerWidth, inputWidth + outputWidth + PortColumnGap);
            widths[node.Id] = Math.Clamp(content, NodeMinWidth, NodeMaxWidth);
        }

        return widths;
    }

    /// <summary>The edges layout is allowed to constrain on: control flow, plus the data flow that is
    /// actually drawn as wires.</summary>
    private static List<(int From, int To)> CollectEdges(ReconstructedGraph graph, Dictionary<string, int> index)
    {
        var edges = new HashSet<(int, int)>();

        foreach (GraphEdge edge in graph.Edges)
        {
            if (edge.Target == EdgeTarget.Node
                && edge.TargetNodeId is not null
                && index.TryGetValue(edge.SourceNodeId, out int from)
                && index.TryGetValue(edge.TargetNodeId, out int to)
                && from != to)
            {
                edges.Add((from, to));
            }
        }

        // Ordinary data edges pull a producer left of its consumer, so a box that only supplies a value
        // sits near what uses it. Hub pins are left out - see this class's remarks.
        HashSet<(string, string)> hubs = DataHubs.Find(graph);
        foreach (DataEdge edge in graph.DataEdges)
        {
            if (edge.SourceNodeId is not null
                && !edge.IsHub(hubs)
                && index.TryGetValue(edge.SourceNodeId, out int from)
                && index.TryGetValue(edge.TargetNodeId, out int to)
                && from != to)
            {
                edges.Add((from, to));
            }
        }

        return [.. edges];
    }

    /// <summary>Weakly-connected components - edge direction ignored, since two boxes joined by a wire
    /// belong on the same band whichever way it points.</summary>
    private static List<List<int>> FindComponents(int nodeCount, List<(int From, int To)> edges)
    {
        var neighbours = new List<int>[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            neighbours[i] = [];
        }
        foreach ((int from, int to) in edges)
        {
            neighbours[from].Add(to);
            neighbours[to].Add(from);
        }

        var seen = new bool[nodeCount];
        var components = new List<List<int>>();

        for (int start = 0; start < nodeCount; start++)
        {
            if (seen[start])
            {
                continue;
            }

            var component = new List<int>();
            var stack = new Stack<int>();
            stack.Push(start);
            seen[start] = true;

            while (stack.Count > 0)
            {
                int node = stack.Pop();
                component.Add(node);
                foreach (int next in neighbours[node])
                {
                    if (!seen[next])
                    {
                        seen[next] = true;
                        stack.Push(next);
                    }
                }
            }

            components.Add(component);
        }

        return components;
    }

    /// <summary>Lays one component out with its top-left at (0,0).</summary>
    private static List<PositionedNode> LayoutComponent(
        ReconstructedGraph graph,
        List<int> component,
        List<(int From, int To)> allEdges,
        Dictionary<string, double> widths)
    {
        var members = component.ToHashSet();
        var edges = allEdges.Where(e => members.Contains(e.From) && members.Contains(e.To)).ToList();

        HashSet<(int, int)> backEdges = FindBackEdges(graph.Nodes.Count, edges);
        var forward = edges.Where(e => !backEdges.Contains(e)).ToList();

        Dictionary<int, int> layer = AssignLayers(component, forward);
        List<List<int>> layers = GroupIntoLayers(component, layer);
        ReduceCrossings(layers, forward);

        return AssignCoordinates(graph, layers, widths);
    }

    /// <summary>Edges that close a cycle, found by depth-first search: an edge into a node currently on
    /// the recursion stack is a back-edge. Iterative rather than recursive because the deepest chains in
    /// this corpus run to a few hundred boxes.</summary>
    private static HashSet<(int, int)> FindBackEdges(int nodeCount, List<(int From, int To)> edges)
    {
        var outgoing = edges.ToLookup(e => e.From, e => e.To);
        var back = new HashSet<(int, int)>();
        var state = new byte[nodeCount]; // 0 = unvisited, 1 = on stack, 2 = done

        foreach (int root in edges.Select(e => e.From).Concat(edges.Select(e => e.To)).Distinct())
        {
            if (state[root] != 0)
            {
                continue;
            }

            var stack = new Stack<(int Node, IEnumerator<int> Children)>();
            state[root] = 1;
            stack.Push((root, outgoing[root].GetEnumerator()));

            while (stack.Count > 0)
            {
                (int node, IEnumerator<int> children) = stack.Peek();
                if (children.MoveNext())
                {
                    int child = children.Current;
                    if (state[child] == 1)
                    {
                        back.Add((node, child));
                    }
                    else if (state[child] == 0)
                    {
                        state[child] = 1;
                        stack.Push((child, outgoing[child].GetEnumerator()));
                    }
                }
                else
                {
                    state[node] = 2;
                    stack.Pop();
                }
            }
        }

        return back;
    }

    /// <summary>Longest-path layering: a node sits one column right of the furthest-right thing feeding
    /// it. Processed in topological order so each node's predecessors are final before it is read.</summary>
    private static Dictionary<int, int> AssignLayers(List<int> component, List<(int From, int To)> edges)
    {
        var layer = component.ToDictionary(n => n, _ => 0);
        var indegree = component.ToDictionary(n => n, _ => 0);
        var outgoing = edges.ToLookup(e => e.From, e => e.To);

        foreach ((_, int to) in edges)
        {
            indegree[to]++;
        }

        var queue = new Queue<int>(component.Where(n => indegree[n] == 0));
        while (queue.Count > 0)
        {
            int node = queue.Dequeue();
            foreach (int child in outgoing[node])
            {
                layer[child] = Math.Max(layer[child], layer[node] + 1);
                if (--indegree[child] == 0)
                {
                    queue.Enqueue(child);
                }
            }
        }

        return layer;
    }

    private static List<List<int>> GroupIntoLayers(List<int> component, Dictionary<int, int> layer)
    {
        int layerCount = component.Max(n => layer[n]) + 1;
        var layers = new List<List<int>>(layerCount);
        for (int i = 0; i < layerCount; i++)
        {
            layers.Add([]);
        }
        foreach (int node in component)
        {
            layers[layer[node]].Add(node);
        }
        return layers;
    }

    /// <summary>
    /// The median heuristic (Eades &amp; Wei): repeatedly reorder one layer by the median position of
    /// each node's neighbours in the adjacent layer. Sweeping forward then backward lets an improvement
    /// at either end propagate through the whole graph.
    ///
    /// A node with no neighbours in the reference layer keeps its current position rather than being
    /// swept to one end, which is what stops isolated boxes from piling up in a corner.
    /// </summary>
    private static void ReduceCrossings(List<List<int>> layers, List<(int From, int To)> edges)
    {
        var predecessors = edges.ToLookup(e => e.To, e => e.From);
        var successors = edges.ToLookup(e => e.From, e => e.To);

        for (int sweep = 0; sweep < OrderingSweeps; sweep++)
        {
            for (int i = 1; i < layers.Count; i++)
            {
                OrderLayer(layers[i], layers[i - 1], predecessors);
            }
            for (int i = layers.Count - 2; i >= 0; i--)
            {
                OrderLayer(layers[i], layers[i + 1], successors);
            }
        }
    }

    private static void OrderLayer(List<int> layer, List<int> reference, ILookup<int, int> neighbours)
    {
        var positionInReference = new Dictionary<int, int>(reference.Count);
        for (int i = 0; i < reference.Count; i++)
        {
            positionInReference[reference[i]] = i;
        }

        var keys = new Dictionary<int, double>(layer.Count);
        for (int i = 0; i < layer.Count; i++)
        {
            int node = layer[i];
            var positions = neighbours[node]
                .Where(positionInReference.ContainsKey)
                .Select(n => (double)positionInReference[n])
                .Order()
                .ToList();

            keys[node] = positions.Count == 0
                ? i // no anchor in the reference layer - leave it where it is
                : positions.Count % 2 == 1
                    ? positions[positions.Count / 2]
                    : (positions[(positions.Count / 2) - 1] + positions[positions.Count / 2]) / 2.0;
        }

        layer.Sort((a, b) => keys[a].CompareTo(keys[b]));
    }

    private static List<PositionedNode> AssignCoordinates(
        ReconstructedGraph graph,
        List<List<int>> layers,
        Dictionary<string, double> widths)
    {
        var positioned = new List<PositionedNode>();
        double x = 0;

        foreach (List<int> layer in layers)
        {
            double y = 0;
            // Columns are spaced by their widest member, so a node that grew to fit a long pin name
            // doesn't run into the next column.
            double columnWidth = layer.Count == 0 ? NodeMinWidth : layer.Max(i => widths[graph.Nodes[i].Id]);

            foreach (int nodeIndex in layer)
            {
                GraphNode node = graph.Nodes[nodeIndex];
                double height = HeightOf(node);
                positioned.Add(new PositionedNode(node, x, y, widths[node.Id], height));
                y += height + RowGap;
            }
            x += columnWidth + ColumnGap;
        }

        return CenterColumnsVertically(positioned);
    }

    /// <summary>Tall columns otherwise hang off the bottom while short ones sit at the top; centering
    /// each column on the same axis keeps an edge between them roughly horizontal.</summary>
    private static List<PositionedNode> CenterColumnsVertically(List<PositionedNode> positioned)
    {
        if (positioned.Count == 0)
        {
            return positioned;
        }

        var byColumn = positioned.GroupBy(p => p.X).ToList();
        double tallest = byColumn.Max(column => column.Sum(p => p.Height) + ((column.Count() - 1) * RowGap));

        var centered = new List<PositionedNode>(positioned.Count);
        foreach (var column in byColumn)
        {
            double columnHeight = column.Sum(p => p.Height) + ((column.Count() - 1) * RowGap);
            double offset = (tallest - columnHeight) / 2;
            centered.AddRange(column.Select(p => p with { Y = p.Y + offset }));
        }
        return centered;
    }

    /// <summary>Boxes connected to nothing at all - 14 of them in `a1bu00_storymission`. A band each
    /// would be absurd, so they go in a grid under everything else.</summary>
    private static IEnumerable<PositionedNode> PackSingletons(
        ReconstructedGraph graph,
        List<int> singletons,
        double top,
        Dictionary<string, double> widths)
    {
        double cell = singletons.Count == 0 ? NodeMinWidth : singletons.Max(i => widths[graph.Nodes[i].Id]);

        for (int i = 0; i < singletons.Count; i++)
        {
            GraphNode node = graph.Nodes[singletons[i]];
            double x = (i % SingletonsPerRow) * (cell + ColumnGap);
            double y = top + ((i / SingletonsPerRow) * (MinNodeHeight + RowGap));
            yield return new PositionedNode(node, x, y, widths[node.Id], HeightOf(node));
        }
    }

    /// <summary>Tall enough for every port to get its own row, since nodify anchors each connector at
    /// its own vertical position - undersize the node and the ports overlap.</summary>
    private static double HeightOf(GraphNode node)
    {
        int inputs = (node.Signature?.ControlIns.Count ?? 1) + (node.Signature?.DataIns.Count ?? 0);
        int outputs = (node.Signature?.ControlOuts.Count ?? 1) + (node.Signature?.DataOuts.Count ?? 0);
        return Math.Max(MinNodeHeight, HeaderHeight + (Math.Max(inputs, outputs) * PortHeight) + 10);
    }
}
