using JackAll.Tools.Domino.Graphs;

namespace JackAll.App.Domino;

/// <summary>One node positioned for display, sized to fit the ports it actually has.</summary>
public sealed record PositionedNode(GraphNode Node, double X, double Y, double Width, double Height);

/// <summary>
/// Layered ("Sugiyama") graph layout: assign each node to a column, order the nodes within each column
/// to minimize edge crossings, then give them coordinates.
///
/// The ordering pass is the part that matters. The original box positions are gone with the
/// `.domino.xml` that held them, so everything here is generated - and simply stacking each column in
/// declaration order (what this replaced) crosses badly on anything past a dozen boxes, which is most
/// of the corpus. Sweeping the median heuristic up and down the layers a few times fixes that cheaply.
///
/// Cycles are real in mission logic - a gameplay loop wiring back to an earlier box - so back-edges are
/// detected up front and reversed for the duration of layering, then flipped back. Without that, layer
/// assignment either doesn't terminate or silently truncates the graph.
/// </summary>
public static class SugiyamaLayout
{
    private const double ColumnGap = 110;
    private const double RowGap = 28;
    private const double NodeWidth = 210;
    private const double HeaderHeight = 34;
    private const double PortHeight = 17;
    private const double MinNodeHeight = 56;

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

        // Layering has to run on a DAG, so take back-edges out first.
        HashSet<(int, int)> backEdges = FindBackEdges(graph.Nodes.Count, edges);
        var forward = edges.Where(e => !backEdges.Contains(e)).ToList();

        int[] layer = AssignLayers(graph.Nodes.Count, forward);
        List<List<int>> layers = GroupIntoLayers(layer);
        ReduceCrossings(layers, forward);

        return AssignCoordinates(graph, layers);
    }

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

        // Data edges pull a producer left of its consumer too - without them a box that only supplies a
        // value (and never fires anything) floats in column 0 far from everything that uses it.
        foreach (DataEdge edge in graph.DataEdges)
        {
            if (edge.SourceNodeId is not null
                && index.TryGetValue(edge.SourceNodeId, out int from)
                && index.TryGetValue(edge.TargetNodeId, out int to)
                && from != to)
            {
                edges.Add((from, to));
            }
        }

        return [.. edges];
    }

    /// <summary>Edges that close a cycle, found by depth-first search: an edge into a node currently on
    /// the recursion stack is a back-edge. Iterative rather than recursive because the deepest chains in
    /// this corpus run to a few hundred boxes.</summary>
    private static HashSet<(int, int)> FindBackEdges(int nodeCount, List<(int From, int To)> edges)
    {
        var outgoing = edges.ToLookup(e => e.From, e => e.To);
        var back = new HashSet<(int, int)>();
        var state = new byte[nodeCount]; // 0 = unvisited, 1 = on stack, 2 = done

        for (int root = 0; root < nodeCount; root++)
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
    private static int[] AssignLayers(int nodeCount, List<(int From, int To)> edges)
    {
        var layer = new int[nodeCount];
        var indegree = new int[nodeCount];
        var outgoing = edges.ToLookup(e => e.From, e => e.To);

        foreach ((_, int to) in edges)
        {
            indegree[to]++;
        }

        var queue = new Queue<int>(Enumerable.Range(0, nodeCount).Where(n => indegree[n] == 0));
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

    private static List<List<int>> GroupIntoLayers(int[] layer)
    {
        int layerCount = layer.Length == 0 ? 0 : layer.Max() + 1;
        var layers = new List<List<int>>(layerCount);
        for (int i = 0; i < layerCount; i++)
        {
            layers.Add([]);
        }
        for (int node = 0; node < layer.Length; node++)
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

    private static List<PositionedNode> AssignCoordinates(ReconstructedGraph graph, List<List<int>> layers)
    {
        var positioned = new List<PositionedNode>(graph.Nodes.Count);
        double x = 0;

        foreach (List<int> layer in layers)
        {
            double y = 0;
            double widest = NodeWidth;

            foreach (int nodeIndex in layer)
            {
                GraphNode node = graph.Nodes[nodeIndex];
                double height = HeightOf(node);
                positioned.Add(new PositionedNode(node, x, y, NodeWidth, height));
                y += height + RowGap;
            }

            x += widest + ColumnGap;
        }

        return CenterColumnsVertically(positioned, layers.Count);
    }

    /// <summary>Tall columns otherwise hang off the bottom while short ones sit at the top; centering
    /// each column on the same axis keeps an edge between them roughly horizontal.</summary>
    private static List<PositionedNode> CenterColumnsVertically(List<PositionedNode> positioned, int layerCount)
    {
        if (layerCount == 0)
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

    /// <summary>Tall enough for every port to get its own row, since nodify anchors each connector at
    /// its own vertical position - undersize the node and the ports overlap.</summary>
    private static double HeightOf(GraphNode node)
    {
        int inputs = (node.Signature?.ControlIns.Count ?? 1) + (node.Signature?.DataIns.Count ?? 0);
        int outputs = (node.Signature?.ControlOuts.Count ?? 1) + (node.Signature?.DataOuts.Count ?? 0);
        return Math.Max(MinNodeHeight, HeaderHeight + (Math.Max(inputs, outputs) * PortHeight) + 10);
    }
}
