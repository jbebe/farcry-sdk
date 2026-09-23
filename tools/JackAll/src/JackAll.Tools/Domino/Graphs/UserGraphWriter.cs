using System.Text;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino.Graphs;

/// <summary>
/// Renders a classified <see cref="UserGraph"/> back to Lua source text - the reverse of
/// <see cref="UserGraphParser"/>.
///
/// <see cref="Write"/> keeps the source: a statement, function header or file-level line that still says
/// what it was parsed from comes out byte for byte, trivia included, so an untouched file round-trips
/// exactly and an edited one differs only where it was edited. Anything built or changed in code is
/// written in the shape BlackBox's own codegen emits. <see cref="WriteCanonical"/> writes every statement
/// that way, which is what proves the shapes themselves lose nothing.
/// </summary>
public static class UserGraphWriter
{
    public static string Write(UserGraph graph) => Write(graph, keepSource: true);

    public static string WriteCanonical(UserGraph graph) => Write(graph, keepSource: false);

    private static string Write(UserGraph graph, bool keepSource)
    {
        string newline = keepSource ? NewlineOf(graph) : "\n";

        // Interleaved in original document order, because a comment is the next statement's leading
        // trivia and grouping by kind would move it to a different neighbour.
        var items = graph.TopLevelOther
            .Select(stmt => (Position: stmt.SpanStart, Render: (Action<StringBuilder>)(sb => WriteTopLevel(sb, stmt, keepSource))))
            .Concat(graph.Functions.Select(fn => (Position: fn.SpanStart, Render: (Action<StringBuilder>)(sb => WriteFunction(sb, fn, keepSource, newline)))))
            .OrderBy(item => item.Position);

        var sb = new StringBuilder();
        foreach (var item in items)
        {
            item.Render(sb);
        }
        if (keepSource)
        {
            sb.Append(graph.EndOfFile);
        }
        return sb.ToString();
    }

    private static void WriteTopLevel(StringBuilder sb, StatementSyntax stmt, bool keepSource)
    {
        if (keepSource)
        {
            sb.Append(stmt.ToFullString());
        }
        else
        {
            sb.Append(stmt.ToFullString().Replace("\r\n", "\n").TrimEnd()).Append('\n');
        }
    }

    private static void WriteFunction(StringBuilder sb, UserGraphFunction fn, bool keepSource, string newline)
    {
        FunctionDeclarationStatementSyntax? decl = keepSource ? fn.Syntax : null;
        string full = decl?.ToFullString() ?? "";

        if (decl is not null && UserGraphParser.ReadHeader(decl) is { } header
            && header.Name == fn.Name && header.Parameters.SequenceEqual(fn.Parameters))
        {
            sb.Append(full, 0, decl.Body.FullSpan.Start - decl.FullSpan.Start);
        }
        else
        {
            if (keepSource)
            {
                sb.Append(newline);
            }
            sb.Append("function export:").Append(fn.Name).Append('(').Append(string.Join(", ", fn.Parameters)).Append(')').Append(newline);
        }

        foreach (UserGraphStmt stmt in fn.Body)
        {
            if (keepSource && IsUnchanged(stmt))
            {
                sb.Append(stmt.Syntax!.ToFullString());
            }
            else
            {
                sb.Append('\t').Append(Canonical(stmt).Replace("\n", newline)).Append(newline);
            }
        }

        if (decl is not null)
        {
            sb.Append(full, decl.Body.FullSpan.End - decl.FullSpan.Start, decl.FullSpan.End - decl.Body.FullSpan.End);
        }
        else
        {
            sb.Append("end;").Append(newline);
        }
    }

    /// <summary>True when a statement still says exactly what the source it was parsed from says.</summary>
    private static bool IsUnchanged(UserGraphStmt stmt) =>
        stmt.Syntax is { } syntax && Canonical(UserGraphParser.Classify(syntax)) == Canonical(stmt);

    /// <summary>One statement in the shape BlackBox's codegen emits, without trivia.</summary>
    public static string Canonical(UserGraphStmt stmt) => stmt switch
    {
        RegisterBoxStmt s => $"cbox:RegisterBox({Str(s.Path)});",
        CreateBoxStmt s => $"{Ref(s.Box)} = cbox:CreateBox({Str(s.Path)});",
        RebindSelfToGraphStmt => "self = self._graph;",
        SetGraphBackrefStmt s => $"{Ref(s.Box)}._graph = self;",
        SetDynamicAnchorsStmt s => $"{Ref(s.Box)}._DynamicAnchors = {{\n{string.Concat(s.Counts.Select(c => $"\t\t{c.Key} = {c.Value},\n"))}\t}};",
        SetParamStmt s => $"{Ref(s.Box)}.{s.ParamName} = {s.Value};",
        WireControlOutStmt s => $"{Ref(s.Box)}.{s.PinName}{(s.Index is { } i ? $"[{i}]" : "")} = {WireTarget(s.TargetHandler)};",
        FireControlInStmt s => $"{Ref(s.Box)}._type.{s.PinName}({Ref(s.Box)}{(s.Index is { } i ? $", {i}" : "")});",
        CallOwnHandlerStmt s => $"self._type.{s.HandlerName}(self);",
        FireOwnPinStmt s => $"self:{s.PinName}();",
        LoadResourceStmt s => $"cbox:LoadResource({Str(s.ResourceName)}, {Str(s.ResourceType)});",
        TraceConnectionStmt s => $"CDominoManager_GetInstance():TraceConnection({Str(s.DocumentContainer)}, {Str(s.SourcePinLabel)}, " +
                                  $"{Str(s.TargetPinLabel)}, {s.SourceBoxExpr}, {s.TargetBoxExpr});",
        ReadDataStmt s => $"{s.Target} = {Ref(s.Box)}.{s.PinName};",
        SetGraphFieldStmt s => $"self.{s.FieldName} = {s.Value};",
        OtherStmt s => s.Statement.ToString().Trim(),
        _ => throw new NotSupportedException($"Unknown UserGraphStmt: {stmt.GetType().Name}"),
    };

    private static string NewlineOf(UserGraph graph)
    {
        string? sample = graph.Functions.Select(fn => fn.Syntax?.ToFullString()).FirstOrDefault(text => text is not null)
            ?? graph.TopLevelOther.FirstOrDefault()?.ToFullString();
        return sample?.Contains("\r\n", StringComparison.Ordinal) == true ? "\r\n" : "\n";
    }

    private static string WireTarget(string? targetHandler) =>
        targetHandler is null ? "DummyFunction" : $"self._type.{targetHandler}";

    internal static string Ref(BoxRef box) => box switch
    {
        InstanceBoxRef i => $"self[{i.Slot}]",
        NamedInstanceBoxRef n => $"self.{n.FieldName}",
        PooledBoxRef p => $"Boxes[PathID({Str(p.Path)})]",
        _ => throw new NotSupportedException($"Unknown BoxRef: {box.GetType().Name}"),
    };

    private static string Str(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
