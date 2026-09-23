using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino.Graphs;

/// <summary>What one statement did to the graph's data, once its box reference has been resolved to a
/// reconstructed node.</summary>
public enum DataEventKind
{
    /// <summary>`self.Var = self[N].Pin;` - box N's data-out pin wrote graph variable Var.</summary>
    Produce,

    /// <summary>`self[N].Param = self.Var;` - box N's data-in parameter read graph variable Var.</summary>
    Consume,

    /// <summary>`self[14].Entity = self[8].ObjectEntity;` - box to box with no variable in between.</summary>
    DirectConsume,
}

/// <summary>One data-relevant statement, emitted by <see cref="GraphBuilder"/> which is where box
/// references resolve to node IDs. <paramref name="Order"/> is a whole-file sequence number so the
/// resolver can prefer a producer that ran earlier in the same handler.</summary>
public sealed record DataEvent(
    DataEventKind Kind,
    string NodeId,
    string Pin,
    string? Variable,
    string? SourceNodeId,
    string? SourcePin,
    string FunctionName,
    int Order);

/// <summary>
/// Joins <see cref="DataEvent"/>s into <see cref="DataEdge"/>s, resolving the graph-variable
/// indirection that hides nearly all of Domino's data flow.
///
/// Values move box → graph variable → box, in two separate handlers, so neither statement on its own is
/// an edge. `self.BuddyPawn = self[29].SpawnedBuddy;` in one function and `self[18].Pawn =
/// self.BuddyPawn;` in another together mean "box 29's SpawnedBuddy feeds box 18's Pawn" - roughly
/// 1,700 producer reads and 5,300 consumer writes across the corpus, none of which the graph model saw
/// before.
///
/// Attribution rule, in order:
/// <list type="number">
/// <item>A producer earlier in the <em>same</em> handler is the answer - the read-then-use idiom, and
/// unambiguous.</item>
/// <item>Otherwise control flow decides, as reaching definitions: walking control edges backwards from
/// the consumer, every writer met before another writer of the same variable is a source. Writers on
/// different branches are all sources, each for the path through it; only when no control path from any
/// writer arrives is the edge ambiguous.</item>
/// <item>No producer at all means the variable is a graph input
/// (<see cref="DataEdgeKind.GraphInput"/>).</item>
/// </list>
/// </summary>
public static class DataFlowResolver
{
    /// <param name="controlEdges">Node-to-node control flow, which decides which writers reach a
    /// consumer. Pass an empty list to fall back on statement order alone.</param>
    public static IReadOnlyList<DataEdge> Resolve(
        IReadOnlyList<DataEvent> events,
        IReadOnlyList<(string From, string To)> controlEdges)
    {
        var producersByVariable = events
            .Where(e => e.Kind == DataEventKind.Produce && e.Variable is not null)
            .ToLookup(e => e.Variable!, StringComparer.Ordinal);

        var predecessors = controlEdges.ToLookup(e => e.To, e => e.From, StringComparer.Ordinal);

        var edges = new List<DataEdge>();

        foreach (DataEvent consumer in events)
        {
            switch (consumer.Kind)
            {
                case DataEventKind.DirectConsume:
                    edges.Add(new DataEdge(
                        consumer.SourceNodeId, consumer.SourcePin, consumer.NodeId, consumer.Pin,
                        ViaVariable: null, DataEdgeKind.NodeToNode, Ambiguous: false));
                    break;

                case DataEventKind.Consume when consumer.Variable is { } variable:
                    edges.AddRange(ResolveThroughVariable(consumer, variable, producersByVariable, predecessors));
                    break;
            }
        }

        return edges;
    }

    private static IEnumerable<DataEdge> ResolveThroughVariable(
        DataEvent consumer,
        string variable,
        ILookup<string, DataEvent> producersByVariable,
        ILookup<string, string> predecessors)
    {
        var candidates = producersByVariable[variable].ToList();
        if (candidates.Count == 0)
        {
            // Nothing in this graph writes it, so it arrives from the parent graph.
            return [new DataEdge(null, null, consumer.NodeId, consumer.Pin, variable, DataEdgeKind.GraphInput, Ambiguous: false)];
        }

        // Rule 1: the read-then-use idiom - the nearest producer earlier in the same handler wins
        // outright, no ambiguity to report.
        DataEvent? sameFunction = candidates
            .Where(p => string.Equals(p.FunctionName, consumer.FunctionName, StringComparison.Ordinal) && p.Order < consumer.Order)
            .OrderByDescending(p => p.Order)
            .FirstOrDefault();

        if (sameFunction is not null)
        {
            return [Edge(sameFunction, consumer, variable, ambiguous: false)];
        }

        // Deduped by (node, pin): the same box writing the same variable from two handlers is one
        // producer, not two.
        var distinct = candidates
            .GroupBy(p => (p.NodeId, p.Pin))
            .Select(g => g.First())
            .ToList();

        if (distinct.Count == 1)
        {
            return [Edge(distinct[0], consumer, variable, ambiguous: false)];
        }

        // Rule 2: the writers whose value can still be in the variable when some control path arrives
        // here - each one a real source, on its own branch.
        var reaching = ReachingProducers(consumer.NodeId, distinct, predecessors);
        if (reaching.Count == 0)
        {
            // No control path from any writer reaches here - nothing to choose between.
            return distinct.Select(p => Edge(p, consumer, variable, ambiguous: true));
        }
        return reaching.Select(p => Edge(p, consumer, variable, ambiguous: false) with { SourceOccurrences = reaching.Count });
    }

    private static DataEdge Edge(DataEvent producer, DataEvent consumer, string variable, bool ambiguous) =>
        new(producer.NodeId, producer.Pin, consumer.NodeId, consumer.Pin, variable, DataEdgeKind.NodeToNode, ambiguous);

    /// <summary>The writers met walking control edges backwards from <paramref name="consumer"/>, the walk
    /// stopping at each writer since an earlier value is overwritten there.</summary>
    private static List<DataEvent> ReachingProducers(string consumer, List<DataEvent> producers, ILookup<string, string> predecessors)
    {
        var byNode = producers.ToLookup(p => p.NodeId, StringComparer.Ordinal);
        var reaching = new List<DataEvent>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(predecessors[consumer]);

        while (queue.Count > 0)
        {
            string node = queue.Dequeue();
            if (!seen.Add(node))
            {
                continue;
            }
            if (byNode.Contains(node))
            {
                reaching.AddRange(byNode[node]);
                continue;
            }
            foreach (string previous in predecessors[node])
            {
                queue.Enqueue(previous);
            }
        }
        return reaching;
    }

    /// <summary>Classifies a `Box.Param = value;` assignment's right-hand side. Returns the graph
    /// variable it reads (`self.Var`), the box pin it reads directly (`self[8].ObjectEntity`), or
    /// neither for a literal - literals stay parameters and are shown in the inspector, not drawn as
    /// wires.</summary>
    public static (string? Variable, (BoxRef Box, string Pin)? DirectSource) ClassifyParamValue(ExpressionSyntax value)
    {
        if (Nodes.DominoNodeCatalog.GraphFieldName(value) is { } variable)
        {
            return (variable, null);
        }
        if (UserGraphParser.TryParseBoxPinRead(value) is { } direct)
        {
            return (null, direct);
        }
        return (null, null);
    }
}
