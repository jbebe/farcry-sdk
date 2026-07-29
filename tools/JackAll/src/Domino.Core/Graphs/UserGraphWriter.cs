using System.Text;
using Domino.Core.Lua;

namespace Domino.Core.Graphs;

/// <summary>
/// Renders a classified <see cref="UserGraph"/> back to Lua source text - the reverse of
/// <see cref="UserGraphParser"/>, emitting the same mechanical statement shapes BlackBox's own codegen
/// produces (see the shape docs on each <see cref="UserGraphStmt"/> subtype) rather than a general Lua
/// pretty-printer. <see cref="OtherStmt"/> and <see cref="UserGraph.TopLevelOther"/> fall back to
/// <see cref="LuaWriter"/> for anything outside the closed vocabulary, so nothing recognized-but-unusual
/// is silently dropped.
///
/// Formatting doesn't have to match a real file byte-for-byte (see <see cref="LuaWriter"/>'s remarks -
/// same reasoning applies here), and statement order across <see cref="UserGraph.TopLevelOther"/> and
/// <see cref="UserGraph.Functions"/> isn't preserved relative to each other (TopLevelOther is emitted
/// first, in its own original order, then every function) - harmless for a script whose statements are
/// either simple globals (`_compilerVersion = 3;`) or independent function declarations with no
/// load-order dependency on one another.
/// </summary>
public static class UserGraphWriter
{
    public static string Write(UserGraph graph)
    {
        var sb = new StringBuilder();
        foreach (LuaStmt stmt in graph.TopLevelOther)
        {
            sb.Append(LuaWriter.WriteStmt(stmt));
        }
        foreach (UserGraphFunction fn in graph.Functions)
        {
            WriteFunction(sb, fn);
        }
        return sb.ToString();
    }

    private static void WriteFunction(StringBuilder sb, UserGraphFunction fn)
    {
        sb.Append("function export:").Append(fn.Name).Append('(').Append(string.Join(", ", fn.Parameters)).Append(")\n");
        foreach (UserGraphStmt stmt in fn.Body)
        {
            sb.Append('\t').Append(WriteGraphStmt(stmt)).Append('\n');
        }
        sb.Append("end;\n");
    }

    private static string WriteGraphStmt(UserGraphStmt stmt) => stmt switch
    {
        RegisterBoxStmt s => $"cbox:RegisterBox({Str(s.Path)});",
        CreateBoxStmt s => $"{Ref(s.Box)} = cbox:CreateBox({Str(s.Path)});",
        RebindSelfToGraphStmt => "self = self._graph;",
        SetGraphBackrefStmt s => $"{Ref(s.Box)}._graph = self;",
        SetParamStmt s => $"{Ref(s.Box)}.{s.ParamName} = {LuaWriter.WriteExpr(s.Value)};",
        WireControlOutStmt s => $"{Ref(s.Box)}.{s.PinName}{(s.Index is { } i ? $"[{i}]" : "")} = {WireTarget(s.TargetHandler)};",
        FireControlInStmt s => $"{Ref(s.Box)}._type.{s.PinName}({Ref(s.Box)});",
        CallOwnHandlerStmt s => $"self._type.{s.HandlerName}(self);",
        FireOwnPinStmt s => $"self:{s.PinName}();",
        LoadResourceStmt s => $"cbox:LoadResource({Str(s.ResourceName)}, {Str(s.ResourceType)});",
        TraceConnectionStmt s => $"CDominoManager_GetInstance():TraceConnection({Str(s.DocumentContainer)}, {Str(s.SourcePinLabel)}, " +
                                  $"{Str(s.TargetPinLabel)}, {LuaWriter.WriteExpr(s.SourceBoxExpr)}, {LuaWriter.WriteExpr(s.TargetBoxExpr)});",
        ReadDataStmt s => $"{LuaWriter.WriteExpr(s.Target)} = {Ref(s.Box)}.{s.PinName};",
        SetGraphFieldStmt s => $"self.{s.FieldName} = {LuaWriter.WriteExpr(s.Value)};",
        OtherStmt s => LuaWriter.WriteStmt(s.Statement).TrimEnd('\n'),
        _ => throw new NotSupportedException($"Unknown UserGraphStmt: {stmt.GetType().Name}"),
    };

    private static string WireTarget(string? targetHandler) =>
        targetHandler is null ? "DummyFunction" : $"self._type.{targetHandler}";

    private static string Ref(BoxRef box) => box switch
    {
        InstanceBoxRef i => $"self[{i.Slot}]",
        NamedInstanceBoxRef n => $"self.{n.FieldName}",
        PooledBoxRef p => $"Boxes[PathID({Str(p.Path)})]",
        _ => throw new NotSupportedException($"Unknown BoxRef: {box.GetType().Name}"),
    };

    private static string Str(string value) => LuaWriter.WriteExpr(new StringExpr(value));
}
