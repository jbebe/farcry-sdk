namespace JackAll.Tools.Domino.Graphs;

/// <summary>The rebuilt visual graph for one `user\` mission graph file - boxes, typed connections, and
/// the file-level metadata (`Create()`'s dependency declarations, direct resource loads) that don't
/// belong to any single box.
///
/// <see cref="Edges"/> is control flow (what fires next); <see cref="DataEdges"/> is data flow (what
/// value comes from where), which the generated code hides behind graph-level variables and
/// <see cref="DataFlowResolver"/> reconstitutes.</summary>
public sealed record ReconstructedGraph(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<DataEdge> DataEdges,
    IReadOnlyList<string> RegisteredDependencies,
    IReadOnlyList<(string Name, string Type)> LoadedResources);
