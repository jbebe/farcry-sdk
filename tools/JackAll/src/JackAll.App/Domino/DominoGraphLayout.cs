using JackAll.Tools.Domino.Graphs;

namespace JackAll.App.Domino;

/// <summary>One node positioned for display.</summary>
public sealed record PositionedNode(GraphNode Node, double X, double Y, double Width, double Height);

/// <summary>
/// A simple layered ("Sugiyama-style") auto-layout: every node's column is its longest-path distance
/// from a root (a node nothing wires into), rows are just declaration order within a column. There is
/// no persisted box position to recover - the original `.domino.xml` that had one is gone - so this
/// only needs to be readable, not a faithful reconstruction of where a human once dragged each box.
/// Cycles (real in mission logic - a gameplay loop wiring back to an earlier box) are broken
/// arbitrarily rather than handled specially: once a node is placed it's never revisited, so a cycle
/// just stops propagating depth past whichever edge closes the loop.
/// </summary>
public static class DominoGraphLayout
{
    private const double ColumnWidth = 260;
    private const double RowHeight = 110;
    public const double NodeWidth = 220;

    public static IReadOnlyList<PositionedNode> Layout(ReconstructedGraph graph)
    {
        Dictionary<string, int> depth = ComputeDepths(graph);

        var byColumn = graph.Nodes
            .GroupBy(n => depth.GetValueOrDefault(n.Id, 0))
            .OrderBy(g => g.Key);

        var positioned = new List<PositionedNode>(graph.Nodes.Count);
        foreach (var column in byColumn)
        {
            int row = 0;
            foreach (GraphNode node in column)
            {
                double height = NodeHeight(node);
                positioned.Add(new PositionedNode(node, column.Key * ColumnWidth, row * RowHeight, NodeWidth, height));
                row++;
            }
        }
        return positioned;
    }

    private static double NodeHeight(GraphNode node) => 50 + Math.Min(node.Params.Count, 6) * 16;

    private static Dictionary<string, int> ComputeDepths(ReconstructedGraph graph)
    {
        var outEdges = graph.Edges
            .Where(e => e.Target == EdgeTarget.Node)
            .ToLookup(e => e.SourceNodeId, e => e.TargetNodeId!);

        var indegree = graph.Nodes.ToDictionary(n => n.Id, _ => 0);
        foreach (var e in graph.Edges.Where(e => e.Target == EdgeTarget.Node))
        {
            indegree[e.TargetNodeId!] = indegree.GetValueOrDefault(e.TargetNodeId!) + 1;
        }

        var depth = new Dictionary<string, int>();
        var remaining = new HashSet<string>(graph.Nodes.Select(n => n.Id));
        var queue = new Queue<string>();
        foreach (GraphNode n in graph.Nodes)
        {
            if (indegree[n.Id] == 0)
            {
                depth[n.Id] = 0;
                queue.Enqueue(n.Id);
            }
        }

        while (remaining.Count > 0)
        {
            if (queue.Count == 0)
            {
                // Nothing left has indegree 0 - only possible if every remaining node is part of a
                // cycle. Seed one arbitrarily so the layout still terminates.
                string seed = remaining.First();
                depth[seed] = 0;
                queue.Enqueue(seed);
            }

            string id = queue.Dequeue();
            if (!remaining.Remove(id))
            {
                continue;
            }

            foreach (string targetId in outEdges[id])
            {
                if (!remaining.Contains(targetId))
                {
                    continue; // already placed - don't let a cycle re-push it
                }

                int candidate = depth[id] + 1;
                depth[targetId] = Math.Max(depth.GetValueOrDefault(targetId, 0), candidate);

                indegree[targetId]--;
                if (indegree[targetId] <= 0)
                {
                    queue.Enqueue(targetId);
                }
            }
        }

        return depth;
    }
}
