using JackAll.Tools.Domino.Graphs;

namespace JackAll.App.FileHandlers.Domino;

/// <summary>
/// Finds the data source pins that feed so many consumers that drawing each one as a wire stops
/// helping. Fan-out in this corpus is extremely concentrated - in `a1bu00_storymission` five of forty
/// source pins carry 58% of the data wires, and in `a1sm01_townescape` three of five carry 93% - so
/// suppressing a handful of pins removes most of the long-distance clutter while leaving ordinary
/// point-to-point data flow drawn as normal.
///
/// Suppressing them loses nothing, because a wire was never how the value actually travelled: Domino
/// routes data through a named graph variable (`self.Player`), and the consumer end is rendered as a
/// chip naming that variable instead. That is arguably a more faithful picture than the wire.
/// </summary>
public static class DataHubs
{
    /// <summary>Distinct consumers at which a source pin stops being drawn as wires. Four is low enough
    /// to catch the real hubs and high enough to leave genuine one-to-few data flow alone.</summary>
    public const int FanOutThreshold = 4;

    public static HashSet<(string NodeId, string Pin)> Find(ReconstructedGraph graph, int threshold = FanOutThreshold) =>
        graph.DataEdges
            .Where(e => e.SourceNodeId is not null && e.SourcePin is not null)
            .GroupBy(e => (NodeId: e.SourceNodeId!, Pin: e.SourcePin!))
            .Where(g => g.Select(e => e.TargetNodeId).Distinct().Count() >= threshold)
            .Select(g => g.Key)
            .ToHashSet();

    public static bool IsHub(this DataEdge edge, HashSet<(string NodeId, string Pin)> hubs) =>
        edge.SourceNodeId is not null
        && edge.SourcePin is not null
        && hubs.Contains((edge.SourceNodeId, edge.SourcePin));
}
