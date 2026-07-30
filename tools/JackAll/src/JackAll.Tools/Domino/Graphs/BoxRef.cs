namespace JackAll.Tools.Domino.Graphs;

/// <summary>Identifies which box instance a `user\` graph statement is operating on.</summary>
public abstract record BoxRef;

/// <summary>`self[N]` — a persistent, per-graph box instance. <paramref name="Slot"/> is a large,
/// effectively-arbitrary integer, very likely the box's original ID from the `.domino.xml` source.</summary>
public sealed record InstanceBoxRef(long Slot) : BoxRef;

/// <summary>`self.box_TypeName_N` — the same persistent-instance idiom as <see cref="InstanceBoxRef"/>,
/// but under a descriptive field name instead of a bare numeric index. Confirmed real (roughly half the
/// corpus's `cbox:CreateBox` call sites use this form instead of `self[N]`) — apparently a BlackBox
/// codegen variant, not an error. Every two-level-deep `self.X.Y` access in the real corpus has `X`
/// prefixed `box_`, with zero exceptions, so this form is unambiguous to recognize structurally.</summary>
public sealed record NamedInstanceBoxRef(string FieldName) : BoxRef;

/// <summary>`Boxes[PathID("Domino/System/X.lua")]` — a shared, reused instance pool slot: one per
/// distinct node-type path referenced anywhere in the graph, reconfigured and re-fired repeatedly.</summary>
public sealed record PooledBoxRef(string Path) : BoxRef;
