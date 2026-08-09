using JackAll.Tools.Domino.Nodes;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino.Graphs;

public enum BoxInstanceKind
{
    /// <summary>`self[N]` / `self.box_TypeName_N` — one node, referenced (and potentially fired
    /// multiple times, from multiple places) throughout the graph's lifetime.</summary>
    Persistent,

    /// <summary>`Boxes[PathID(...)]` — a shared runtime slot, reused and reconfigured repeatedly. Each
    /// distinct configure-and-fire occurrence gets its own reconstructed node here (see
    /// <see cref="GraphBuilder"/>'s remarks), since the original visual graph almost certainly had a
    /// separate box per usage site even though the flattened script reuses one slot. The debug twins
    /// confirm this directly: they name four separate `box_Set_Entity_1..4` boxes in a graph whose
    /// release code only ever mentions one `Boxes[PathID("Domino/System/SetEntity.lua")]`.</summary>
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

    /// <summary>This node type's pin interface, once a <see cref="DominoNodeCatalog"/> has resolved it.
    /// Null when the referenced script couldn't be read - the node still renders, just without ports.
    /// </summary>
    public NodeSignature? Signature { get; init; }

    /// <summary>The box's original name from the editor (`box_Set_Entity_2`), recovered from the debug
    /// twin. Null when there is no twin, or when this is a pooled occurrence the twin can't be lined up
    /// with.</summary>
    public string? OriginalName { get; init; }

    /// <summary>What to show on the node: the editor's own box name when the twin gave us one, else the
    /// node type's display name, else the bare file name.</summary>
    public string DisplayName =>
        OriginalName ?? Signature?.DisplayName ?? NodeSignature.ShortNameFor(NodeTypePath);
}
