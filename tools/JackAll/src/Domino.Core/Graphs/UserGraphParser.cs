using Domino.Core.Lua;

namespace Domino.Core.Graphs;

/// <summary>
/// Classifies a `user\` mission graph's parsed <see cref="LuaChunk"/> into the closed set of statement
/// shapes BlackBox's codegen actually emits (see <see cref="UserGraphStmt"/>). This is a shape
/// recognizer, not a graph builder — it does not resolve `f_N_...` handler names into edges or dedupe
/// `Boxes[PathID(...)]` occurrences into nodes; that's graph reconstruction (Phase 2), built on top of
/// this classified, statement-level structure.
/// </summary>
public static class UserGraphParser
{
    public static UserGraph Parse(LuaChunk chunk)
    {
        var functions = new List<UserGraphFunction>();
        var topLevelOther = new List<LuaStmt>();

        foreach (var stmt in chunk.Statements)
        {
            if (stmt is FunctionDeclStmt { NamePath: ["export", var name] } fn)
            {
                var body = fn.Body.Select(ClassifyStmt).ToList();
                functions.Add(new UserGraphFunction(name, fn.Parameters, body));
            }
            else
            {
                topLevelOther.Add(stmt);
            }
        }

        return new UserGraph(functions, topLevelOther);
    }

    private static UserGraphStmt ClassifyStmt(LuaStmt stmt)
    {
        switch (stmt)
        {
            // cbox:RegisterBox("Domino/System/X.lua");
            case CallStmt { Call: MethodCallExpr { Target: NameExpr { Name: "cbox" }, Method: "RegisterBox", Args: [StringExpr path] } }:
                return new RegisterBoxStmt(path.Value);

            // self._type.HandlerName(self);  (own en_N/ex_N/OnEnter_.../OnExit_... helper)
            case CallStmt { Call: CallExpr { Callee: FieldAccessExpr { Target: FieldAccessExpr { Target: NameExpr { Name: "self" }, Field: "_type" }, Field: var ownHandler }, Args: [NameExpr { Name: "self" }] } }:
                return new CallOwnHandlerStmt(ownHandler);

            // Box._type.PinName(Box);
            case CallStmt { Call: CallExpr { Callee: FieldAccessExpr { Target: FieldAccessExpr { Field: "_type" } typeTarget, Field: string pinName } } }
                when TryParseBoxRef(typeTarget.Target) is { } fireBox:
                return new FireControlInStmt(fireBox, pinName);

            // self:PinName();  (fire own exposed control-out pin)
            case CallStmt { Call: MethodCallExpr { Target: NameExpr { Name: "self" }, Method: var ownPin, Args: [] } }:
                return new FireOwnPinStmt(ownPin);

            // cbox:LoadResource("name", "CResourceType");
            case CallStmt { Call: MethodCallExpr { Target: NameExpr { Name: "cbox" }, Method: "LoadResource", Args: [StringExpr resName, StringExpr resType] } }:
                return new LoadResourceStmt(resName.Value, resType.Value);

            // CDominoManager_GetInstance():TraceConnection("doc", "src.Pin", "dst.Pin", srcBox, dstBox);  (debug builds only)
            case CallStmt {
                Call: MethodCallExpr {
                    Target: CallExpr { Callee: NameExpr { Name: "CDominoManager_GetInstance" }, Args: [] },
                    Method: "TraceConnection",
                    Args: [StringExpr doc, StringExpr srcPin, StringExpr dstPin, var srcBoxExpr, var dstBoxExpr],
                },
            }:
                return new TraceConnectionStmt(doc.Value, srcPin.Value, dstPin.Value, srcBoxExpr, dstBoxExpr);

            // self[N] = cbox:CreateBox("path");  /  self.box_TypeName_N = cbox:CreateBox("path");
            case AssignStmt { Targets: [var t], Values: [MethodCallExpr { Target: NameExpr { Name: "cbox" }, Method: "CreateBox", Args: [StringExpr createPath] }] }
                when TryParseBoxRef(t) is { } createdBox:
                return new CreateBoxStmt(createdBox, createPath.Value);

            // self = self._graph;
            case AssignStmt { Targets: [NameExpr { Name: "self" }], Values: [FieldAccessExpr { Target: NameExpr { Name: "self" }, Field: "_graph" }] }:
                return new RebindSelfToGraphStmt();

            // Box._graph = self;
            case AssignStmt { Targets: [FieldAccessExpr { Field: "_graph" } graphTarget], Values: [NameExpr { Name: "self" }] }
                when TryParseBoxRef(graphTarget.Target) is { } backrefBox:
                return new SetGraphBackrefStmt(backrefBox);

            // Box.PinName[N] = self._type.f_N_...;  /  Box.PinName[N] = DummyFunction;  (dynamic control-out pin)
            case AssignStmt { Targets: [IndexAccessExpr { Target: FieldAccessExpr { Field: var dynPinName } dynPinTarget, Key: NumberExpr dynIdx }], Values: [var dynValue] }
                when TryParseBoxRef(dynPinTarget.Target) is { } dynBox
                    && (dynValue is NameExpr { Name: "DummyFunction" } || TryParseSelfTypeHandler(dynValue) is not null):
                return new WireControlOutStmt(dynBox, dynPinName, (int)ParseIntLiteral(dynIdx.Raw), ResolveWireTarget(dynValue));

            // Box.PinName = self._type.f_N_...;  /  Box.PinName = DummyFunction;  /  Box.ParamName = value;
            case AssignStmt { Targets: [FieldAccessExpr { Field: var fieldName } fieldTarget], Values: [var value] }
                when TryParseBoxRef(fieldTarget.Target) is { } fieldBox:
                if (value is NameExpr { Name: "DummyFunction" } || TryParseSelfTypeHandler(value) is not null)
                {
                    return new WireControlOutStmt(fieldBox, fieldName, null, ResolveWireTarget(value));
                }
                return new SetParamStmt(fieldBox, fieldName, value);

            // Target = Box.PinName;  (reading a data-out value into graph state)
            case AssignStmt { Targets: [var readTarget], Values: [FieldAccessExpr { Field: var readPin } readSource] }
                when TryParseBoxRef(readSource.Target) is { } readBox:
                return new ReadDataStmt(readTarget, readBox, readPin);

            // self.FieldName = value;  (plain graph-level variable init, not a box operation)
            case AssignStmt { Targets: [FieldAccessExpr { Target: NameExpr { Name: "self" }, Field: var graphFieldName }], Values: [var graphFieldValue] }:
                return new SetGraphFieldStmt(graphFieldName, graphFieldValue);

            default:
                return new OtherStmt(stmt);
        }
    }

    /// <summary>Recognizes `self[N]`, `self.box_TypeName_N`, or `Boxes[PathID("path")]`.</summary>
    private static BoxRef? TryParseBoxRef(LuaExpr expr) => expr switch
    {
        IndexAccessExpr { Target: NameExpr { Name: "self" }, Key: NumberExpr num } => new InstanceBoxRef(ParseIntLiteral(num.Raw)),
        FieldAccessExpr { Target: NameExpr { Name: "self" }, Field: var name } when name.StartsWith("box_", StringComparison.Ordinal) => new NamedInstanceBoxRef(name),
        IndexAccessExpr { Target: NameExpr { Name: "Boxes" }, Key: CallExpr { Callee: NameExpr { Name: "PathID" }, Args: [StringExpr path] } } => new PooledBoxRef(path.Value),
        _ => null,
    };

    /// <summary>Recognizes `self._type.HandlerName`.</summary>
    private static string? TryParseSelfTypeHandler(LuaExpr expr) =>
        expr is FieldAccessExpr { Target: FieldAccessExpr { Target: NameExpr { Name: "self" }, Field: "_type" }, Field: var handler } ? handler : null;

    /// <summary>A control-out wiring value is either `self._type.HandlerName` (wired) or `DummyFunction`
    /// (unwired) — this is only called once one of those two shapes is already confirmed.</summary>
    private static string? ResolveWireTarget(LuaExpr value) => TryParseSelfTypeHandler(value);

    private static long ParseIntLiteral(string raw) => long.Parse(raw);
}
