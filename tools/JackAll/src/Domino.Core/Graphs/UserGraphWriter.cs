using System.Text;

namespace Domino.Core.Graphs;

/// <summary>
/// Renders a classified <see cref="UserGraph"/> back to Lua source text - the reverse of
/// <see cref="UserGraphParser"/>, emitting the same mechanical statement shapes BlackBox's own codegen
/// produces (see the shape docs on each <see cref="UserGraphStmt"/> subtype). <see cref="OtherStmt"/> and
/// <see cref="UserGraph.TopLevelOther"/> fall back to the Loretta node's own <c>ToFullString()</c> for
/// anything outside the closed vocabulary, so nothing recognized-but-unusual is silently dropped -
/// and since that text came from a real parsed node, it already carries its own trivia (comments,
/// formatting) faithfully, no separate pretty-printer needed for the passthrough case.
///
/// Formatting doesn't have to match a real file byte-for-byte - the engine's own Lua interpreter
/// re-parses whatever comes out, so only regenerating an equivalent, loadable file matters, not exact
/// whitespace. Top-level statements (<see cref="UserGraph.TopLevelOther"/> and
/// <see cref="UserGraph.Functions"/> both) are still interleaved back into their original relative
/// order, though, by each item's original <c>SpanStart</c> - not because load order matters (it
/// doesn't; these are simple globals and independent function declarations), but because a comment is
/// another statement's leading trivia, and grouping "every TopLevelOther, then every function" would
/// visibly relocate a comment to a different neighbor than the one it started next to.
/// </summary>
public static class UserGraphWriter
{
    public static string Write(UserGraph graph)
    {
        // Interleaved in original document order (by SpanStart) rather than "every TopLevelOther then
        // every function" - a comment is another statement's leading trivia, so grouping by kind can
        // visibly relocate a comment to a different neighbor than the one it originally belonged to.
        var items = graph.TopLevelOther.Select(stmt => (Position: stmt.SpanStart, Render: (Action<StringBuilder>)(sb => sb.Append(stmt.ToFullString().TrimEnd()).Append('\n'))))
            .Concat(graph.Functions.Select(fn => (Position: fn.SpanStart, Render: (Action<StringBuilder>)(sb => WriteFunction(sb, fn)))))
            .OrderBy(item => item.Position);

        var sb = new StringBuilder();
        foreach (var item in items)
        {
            item.Render(sb);
        }

        // Passthrough content (TopLevelOther/OtherStmt's ToFullString()) carries the original file's
        // \r\n line endings, but every newline this writer adds itself is a bare \n - normalize so the
        // output isn't a mix of both, which is otherwise harmless (Lua doesn't care) but broke this
        // writer's own idempotence check: TrimEnd() on a \r\n-trailing passthrough statement doesn't
        // reliably leave the same boundary as a freshly-generated \n one on a second round trip.
        return sb.ToString().Replace("\r\n", "\n");
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
        SetParamStmt s => $"{Ref(s.Box)}.{s.ParamName} = {s.Value};",
        WireControlOutStmt s => $"{Ref(s.Box)}.{s.PinName}{(s.Index is { } i ? $"[{i}]" : "")} = {WireTarget(s.TargetHandler)};",
        FireControlInStmt s => $"{Ref(s.Box)}._type.{s.PinName}({Ref(s.Box)});",
        CallOwnHandlerStmt s => $"self._type.{s.HandlerName}(self);",
        FireOwnPinStmt s => $"self:{s.PinName}();",
        LoadResourceStmt s => $"cbox:LoadResource({Str(s.ResourceName)}, {Str(s.ResourceType)});",
        TraceConnectionStmt s => $"CDominoManager_GetInstance():TraceConnection({Str(s.DocumentContainer)}, {Str(s.SourcePinLabel)}, " +
                                  $"{Str(s.TargetPinLabel)}, {s.SourceBoxExpr}, {s.TargetBoxExpr});",
        ReadDataStmt s => $"{s.Target} = {Ref(s.Box)}.{s.PinName};",
        SetGraphFieldStmt s => $"self.{s.FieldName} = {s.Value};",
        OtherStmt s => s.Statement.ToFullString().Trim(),
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

    private static string Str(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
