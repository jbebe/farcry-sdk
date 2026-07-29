using Loretta.CodeAnalysis.Lua.Syntax;

namespace Domino.Core.Graphs;

public enum BoxInstanceKind
{
    /// <summary>`self[N]` / `self.box_TypeName_N` — one node, referenced (and potentially fired
    /// multiple times, from multiple places) throughout the graph's lifetime.</summary>
    Persistent,

    /// <summary>`Boxes[PathID(...)]` — a shared runtime slot, reused and reconfigured repeatedly. Each
    /// distinct configure-and-fire occurrence gets its own reconstructed node here (see
    /// <see cref="GraphBuilder"/>'s remarks), since the original visual graph almost certainly had a
    /// separate box per usage site even though the flattened script reuses one slot.</summary>
    Pooled,
}

/// <summary>One reconstructed visual-editor box.</summary>
public sealed record GraphNode(
    string Id,
    BoxRef Ref,
    string NodeTypePath,
    BoxInstanceKind Kind,
    string OwnerFunction,
    IReadOnlyDictionary<string, ExpressionSyntax> Params)
{
    /// <summary>True when this node's type path points at another `user\` graph rather than a
    /// `system\` node - i.e. it's a sub-graph used as a box, and should be rendered as a nestable/
    /// collapsible node rather than a leaf.</summary>
    public bool IsSubGraph => NodeTypePath.StartsWith("Domino/User/", StringComparison.OrdinalIgnoreCase);
}
