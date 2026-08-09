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
    int Order)
{
    /// <summary>The producing box's node type, used to recognize several occurrences of one repeated
    /// operation as a single logical source rather than as competing producers.</summary>
    public string? NodeTypePath { get; init; }
}

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
/// <item>Otherwise control flow decides. The generated code splits producer and consumer across
/// handlers by construction (`f_M_Out` reads box M's output into a variable, then `en_N` pushes it into
/// box N), so the producer is nearly always a box that control flow passes through on the way here.
/// Walking control edges backwards from the consumer and taking the closest producer picks that one
/// out; only a genuine tie at the same distance, or a producer no control path reaches, stays
/// ambiguous.</item>
/// <item>No producer at all means the variable is a graph input
/// (<see cref="DataEdgeKind.GraphInput"/>).</item>
/// </list>
/// </summary>
public static class DataFlowResolver
{
    /// <param name="controlEdges">Node-to-node control flow, used to rank candidate producers by how
    /// far upstream they are. Pass an empty list to fall back on statement order alone.</param>
    public static IReadOnlyList<DataEdge> Resolve(
        IReadOnlyList<DataEvent> events,
        IReadOnlyList<(string From, string To)> controlEdges)
    {
        var producersByVariable = events
            .Where(e => e.Kind == DataEventKind.Produce && e.Variable is not null)
            .ToLookup(e => e.Variable!, StringComparer.Ordinal);

        var predecessors = controlEdges.ToLookup(e => e.To, e => e.From, StringComparer.Ordinal);
        var distanceCache = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

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
                    edges.AddRange(ResolveThroughVariable(consumer, variable, producersByVariable, predecessors, distanceCache));
                    break;
            }
        }

        return edges;
    }

    private static IEnumerable<DataEdge> ResolveThroughVariable(
        DataEvent consumer,
        string variable,
        ILookup<string, DataEvent> producersByVariable,
        ILookup<string, string> predecessors,
        Dictionary<string, Dictionary<string, int>> distanceCache)
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

        Dictionary<string, int> distance = UpstreamDistances(consumer.NodeId, predecessors, distanceCache);

        // Repeated-operation check first: if every writer is the same node type writing the same pin,
        // they are one logical source duplicated across branches, not rival producers. Report the
        // nearest occurrence and record how many there were.
        if (distinct.GroupBy(p => (p.NodeTypePath, p.Pin)).Count() == 1)
        {
            DataEvent nearestOccurrence = distinct
                .OrderBy(p => distance.TryGetValue(p.NodeId, out int d) ? d : int.MaxValue)
                .ThenBy(p => p.Order)
                .First();
            return [Edge(nearestOccurrence, consumer, variable, ambiguous: false) with { SourceOccurrences = distinct.Count }];
        }

        // Rule 2: genuinely different producers - rank by how far upstream each sits in control flow.
        var reachable = distinct.Where(p => distance.ContainsKey(p.NodeId)).ToList();

        if (reachable.Count == 0)
        {
            // No control path from any writer reaches here - nothing to choose between.
            return distinct.Select(p => Edge(p, consumer, variable, ambiguous: true));
        }

        int nearest = reachable.Min(p => distance[p.NodeId]);
        var winners = reachable.Where(p => distance[p.NodeId] == nearest).ToList();
        bool ambiguous = winners.Count > 1;
        return winners.Select(p => Edge(p, consumer, variable, ambiguous));
    }

    private static DataEdge Edge(DataEvent producer, DataEvent consumer, string variable, bool ambiguous) =>
        new(producer.NodeId, producer.Pin, consumer.NodeId, consumer.Pin, variable, DataEdgeKind.NodeToNode, ambiguous);

    /// <summary>Breadth-first distance from every node that can reach <paramref name="target"/> through
    /// control edges. Cached per target because a graph fires the same box from many places, so the same
    /// walk would otherwise repeat once per parameter that box takes.</summary>
    private static Dictionary<string, int> UpstreamDistances(
        string target,
        ILookup<string, string> predecessors,
        Dictionary<string, Dictionary<string, int>> cache)
    {
        if (cache.TryGetValue(target, out Dictionary<string, int>? cached))
        {
            return cached;
        }

        var distance = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<(string Node, int Depth)>();
        queue.Enqueue((target, 0));
        distance[target] = 0;

        while (queue.Count > 0)
        {
            (string node, int depth) = queue.Dequeue();
            foreach (string previous in predecessors[node])
            {
                if (distance.TryAdd(previous, depth + 1))
                {
                    queue.Enqueue((previous, depth + 1));
                }
            }
        }

        cache[target] = distance;
        return distance;
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
