namespace JackAll.App.Domino.Nodes;

/// <summary>The editor-facing label a `system\` node shows, from `&lt;Display Category="..." Text="..."/&gt;`.</summary>
public sealed record NodeDisplay(string Category, string Text);

/// <summary>`&lt;ControlIn Name="..." [Dynamic="True"]/&gt;` — an input execution pin. A dynamic control-in
/// (rare) accepts a runtime-determined number of incoming wires, per <c>self._DynamicAnchors</c>.</summary>
public sealed record ControlInPin(string Name, bool Dynamic);

/// <summary>`&lt;ControlOut Name="..." [Delayed="true"] [Dynamic="True"]/&gt;` — an output execution pin.
/// Delayed pins fire on a later tick rather than synchronously; dynamic pins fan out to a runtime-sized
/// set of targets (see <see cref="ControlInPin.Dynamic"/> and e.g. `outputorder.lua`).</summary>
public sealed record ControlOutPin(string Name, bool Delayed, bool Dynamic);

/// <summary>`&lt;DataIn Name="..." Type="Core|string"/&gt;` — a typed input value pin.</summary>
public sealed record DataInPin(string Name, string Type);

/// <summary>`&lt;DataOut Name="..." Type="Core|string"/&gt;` — a typed output value pin.</summary>
public sealed record DataOutPin(string Name, string Type);

/// <summary>
/// The parsed contents of a `system\` node's `-- DOMINO REFLECTION BOX START ... END` comment block —
/// the node's pin signature, exactly as the visual editor would have shown it. <see cref="Stateless"/>
/// marks a node with no per-instance fields (safe to share a single reused instance across every call
/// site, matching the `Boxes[PathID(...)]` pooling idiom seen in `user\` graphs).
/// </summary>
public sealed record NodeReflection(
    NodeDisplay? Display,
    IReadOnlyList<ControlInPin> ControlIns,
    IReadOnlyList<ControlOutPin> ControlOuts,
    IReadOnlyList<DataInPin> DataIns,
    IReadOnlyList<DataOutPin> DataOuts,
    bool Stateless);
