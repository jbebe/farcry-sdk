using Domino.Core.Lua;

namespace Domino.Core.Graphs;

/// <summary>One classified statement from inside a `user\` graph's generated function body. Every real
/// statement is one of these seven mechanical shapes or, if not, falls back to <see cref="OtherStmt"/> so
/// nothing is silently dropped.</summary>
public abstract record UserGraphStmt;

/// <summary>`cbox:RegisterBox("Domino/System/X.lua");` — a node-type dependency declaration, in `Create()`.</summary>
public sealed record RegisterBoxStmt(string Path) : UserGraphStmt;

/// <summary>`self[N] = cbox:CreateBox("path");` (or `self.box_TypeName_N = cbox:CreateBox("path");`) —
/// instantiates a persistent, per-graph box.</summary>
public sealed record CreateBoxStmt(BoxRef Box, string Path) : UserGraphStmt;

/// <summary>`self = self._graph;` — rebinds the handler's local `self` from the firing box instance back
/// to the owning graph, so subsequent `self.Field = ...` writes land on graph state, not the box.</summary>
public sealed record RebindSelfToGraphStmt : UserGraphStmt;

/// <summary>`Box._graph = self;` — boilerplate box-to-owning-graph back-reference.</summary>
public sealed record SetGraphBackrefStmt(BoxRef Box) : UserGraphStmt;

/// <summary>`Box.ParamName = value;` — sets a data-in parameter on a box instance before firing it.</summary>
public sealed record SetParamStmt(BoxRef Box, string ParamName, LuaExpr Value) : UserGraphStmt;

/// <summary>`Box.PinName = self._type.f_N_...;` (or `DummyFunction` when unconnected) — a graph edge:
/// wires a box's control-out pin to the handler that runs next. <see cref="TargetHandler"/> is null for
/// an unwired (`DummyFunction`) pin. <see cref="Index"/> is set for a `Dynamic="True"` pin wired as
/// `Box.PinName[N] = ...` — a runtime-sized fan-out, one wire per array slot (see `outputorder.lua`).</summary>
public sealed record WireControlOutStmt(BoxRef Box, string PinName, int? Index, string? TargetHandler) : UserGraphStmt;

/// <summary>`Box._type.PinName(Box);` — fires a box's named control-in pin (its entry point).</summary>
public sealed record FireControlInStmt(BoxRef Box, string PinName) : UserGraphStmt;

/// <summary>`self._type.HandlerName(self);` — the graph calling one of its own generated helper
/// functions (an `en_N` parameter setter, an `ex_N` exit helper, or an `OnEnter_box_X`/`OnExit_box_X`
/// hook), as opposed to firing a pin on some other box. Same dispatch shape as
/// <see cref="FireControlInStmt"/> but through the graph's own `_type` table.</summary>
public sealed record CallOwnHandlerStmt(string HandlerName) : UserGraphStmt;

/// <summary>`self:PinName();` — the graph firing one of its own exposed control-out pins (relevant when
/// this graph is itself used as a sub-box by a parent graph). A zero-argument colon call on `self`.</summary>
public sealed record FireOwnPinStmt(string PinName) : UserGraphStmt;

/// <summary>`cbox:LoadResource("name", "CResourceTypeName");` — loads a raw engine resource (animation,
/// sound, movement resource, ...) directly, bypassing the box/pin system entirely.</summary>
public sealed record LoadResourceStmt(string ResourceName, string ResourceType) : UserGraphStmt;

/// <summary>
/// `CDominoManager_GetInstance():TraceConnection("DocumentContainer|...", "box_X.Out", "box_X.In",
/// self.box_X, Boxes[PathID(...)]);` — debug-build-only instrumentation, present in every `*.debug.lua`
/// sibling file (never in the release variant). Valuable rather than noise: it restates the exact edge
/// (human-readable source/target pin label plus the two box expressions) the surrounding
/// wiring/fire statements already encode mechanically, and is a useful cross-check for Phase 2 graph
/// reconstruction. The box expressions are kept raw rather than resolved through <see cref="BoxRef"/>
/// because the source/target here is sometimes bare `self` (the graph's own exposed pin), which no
/// <see cref="BoxRef"/> variant represents.
/// </summary>
public sealed record TraceConnectionStmt(
    string DocumentContainer,
    string SourcePinLabel,
    string TargetPinLabel,
    LuaExpr SourceBoxExpr,
    LuaExpr TargetBoxExpr) : UserGraphStmt;

/// <summary>`Target = Box.PinName;` — reads a box's data-out pin value into a graph-level variable
/// (<paramref name="Target"/> is typically `self.SomeField`, kept as a raw expression for flexibility).</summary>
public sealed record ReadDataStmt(LuaExpr Target, BoxRef Box, string PinName) : UserGraphStmt;

/// <summary>`self.FieldName = value;` — initializes a plain graph-level variable (as opposed to a box's
/// data-in parameter, see <see cref="SetParamStmt"/>), typically in `Init()`.</summary>
public sealed record SetGraphFieldStmt(string FieldName, LuaExpr Value) : UserGraphStmt;

/// <summary>Anything that doesn't match one of the recognized shapes — preserved verbatim rather than
/// dropped, so a file with an unanticipated idiom still round-trips.</summary>
public sealed record OtherStmt(LuaStmt Statement) : UserGraphStmt;

/// <summary>One `function export:Name(...) ... end` handler, its body statements classified.</summary>
public sealed record UserGraphFunction(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<UserGraphStmt> Body);

/// <summary>A fully classified `user\` mission graph file. <see cref="TopLevelOther"/> holds everything
/// outside a function body — the auto-generated header comment, `export = {};`, `_compilerVersion = 3;`.</summary>
public sealed record UserGraph(
    IReadOnlyList<UserGraphFunction> Functions,
    IReadOnlyList<LuaStmt> TopLevelOther);
