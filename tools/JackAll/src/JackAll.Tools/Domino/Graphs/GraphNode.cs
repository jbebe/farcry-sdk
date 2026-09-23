using JackAll.Tools.Domino.Nodes;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino.Graphs;

public enum BoxInstanceKind
{
    /// <summary>`self[N]` / `self.box_TypeName_N` — one box the graph owns for its whole lifetime.</summary>
    Persistent,

    /// <summary>`Boxes[PathID(...)]` — a runtime slot shared by every box of that type, reconfigured before
    /// each fire. Each editor box that used the slot is still its own node.</summary>
    Pooled,
}

/// <summary>How a box's editor ID was worked out.</summary>
public enum BoxIdSource
{
    /// <summary>The persistent box's own `self[N]` slot.</summary>
    Slot,

    /// <summary>A control-out wired to `f_N_Pin`, which names box N.</summary>
    Wire,

    /// <summary>Configured by `en_N`, box N's own prologue.</summary>
    Prologue,

    /// <summary>Fired without being configured, from box N's own continuation `f_N_Pin` or `ex_N`.</summary>
    Continuation,

    /// <summary>Nothing in the release code names it; the debug twin's trace does.</summary>
    Twin,

    /// <summary>Nothing names it and there is no twin to ask.</summary>
    None,
}

/// <summary>One reconstructed visual-editor box.</summary>
public sealed record GraphNode(
    string Id,
    BoxRef Ref,
    string NodeTypePath,
    BoxInstanceKind Kind,
    IReadOnlyDictionary<string, ExpressionSyntax> Params)
{
    /// <summary>The box's ID in the original `.domino.xml`; null when nothing recovers it.</summary>
    public long? EditorId { get; init; }

    public BoxIdSource IdSource { get; init; }

    /// <summary>How many slots each `Dynamic="True"` pin has, from `_DynamicAnchors`.</summary>
    public IReadOnlyDictionary<string, int> DynamicSlots { get; init; } = new Dictionary<string, int>();

    /// <summary>Where in the file each statement that configures, fires or reads this box starts.</summary>
    public IReadOnlyList<int> SourcePositions { get; init; } = [];

    /// <summary>True when this node's type path points at another `user\` graph rather than a
    /// `system\` node - a sub-graph used as a box.</summary>
    public bool IsSubGraph => NodeTypePath.StartsWith("Domino/User/", StringComparison.OrdinalIgnoreCase);

    /// <summary>This node type's pin interface, once a <see cref="DominoNodeCatalog"/> has resolved it.
    /// Null when the referenced script couldn't be read - the node still renders, just without ports.
    /// </summary>
    public NodeSignature? Signature { get; init; }

    /// <summary>The box's original name from the editor (`box_Set_Entity_2`), recovered from the debug
    /// twin.</summary>
    public string? OriginalName { get; init; }

    /// <summary>What kind of box this is, e.g. `Scripted Scene Prefab`.</summary>
    public string TypeTitle => Signature?.Title ?? NodeSignature.ShortNameFor(NodeTypePath);

    /// <summary>Which box of that kind: `box_BRIEFING_SUBVERT_7` → `BRIEFING_SUBVERT  ·  #7`, or just the
    /// editor ID when there is no name.</summary>
    public string InstanceLabel => OriginalName is { } name && DominoDebugTwin.TryParseBoxName(name, out string stem, out long id)
        ? $"{stem}  ·  #{id}"
        : OriginalName ?? (EditorId is { } editorId ? $"#{editorId}" : "#?");
}
