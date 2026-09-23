namespace JackAll.Tools.Domino.Graphs;

/// <summary>The rebuilt visual graph for one `user\` mission graph file - boxes, typed connections, and
/// the file-level metadata (`Create()`'s dependency declarations, direct resource loads) that don't
/// belong to any single box.
///
/// <see cref="Edges"/> is control flow (what fires next); <see cref="DataEdges"/> is data flow (what
/// value comes from where), which the generated code hides behind graph-level variables and
/// <see cref="DataFlowResolver"/> reconstitutes. <see cref="VariableDefaults"/> is the Lua value `Init()`
/// gives each graph variable.</summary>
public sealed record ReconstructedGraph(
    IReadOnlyList<GraphNode> Nodes,
    IReadOnlyList<GraphEdge> Edges,
    IReadOnlyList<DataEdge> DataEdges,
    IReadOnlyList<string> RegisteredDependencies,
    IReadOnlyList<(string Name, string Type)> LoadedResources,
    IReadOnlyDictionary<string, string> VariableDefaults)
{
    /// <summary>Every function in the file and what it is for.</summary>
    public IReadOnlyDictionary<string, GraphFunction> Functions { get; init; } = new Dictionary<string, GraphFunction>();

    /// <summary>What the reconstruction could not make sense of, each a defect in the script or in the
    /// reconstruction.</summary>
    public IReadOnlyList<DominoFinding> Findings { get; init; } = [];

    /// <summary>The reconstruction checked against the debug twin, when there was one.</summary>
    public TwinValidation? Twin { get; init; }

    public IEnumerable<GraphFunction> EntryPins => Functions.Values.Where(f => f.Role == FunctionRole.Entry);

    public IEnumerable<GraphFunction> OutAnchors => Functions.Values.Where(f => f.Role == FunctionRole.OutAnchor);
}
