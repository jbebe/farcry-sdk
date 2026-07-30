namespace JackAll.App.Domino.Graphs;

/// <summary>The rebuilt visual graph for one `user\` mission graph file - boxes, typed connections, and
/// the file-level metadata (`Create()`'s dependency declarations, direct resource loads) that don't
/// belong to any single box.</summary>
public sealed record ReconstructedGraph(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<string> RegisteredDependencies,
    IReadOnlyList<(string Name, string Type)> LoadedResources);
