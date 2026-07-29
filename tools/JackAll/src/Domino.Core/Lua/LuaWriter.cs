using System.Text;

namespace Domino.Core.Lua;

/// <summary>
/// Renders a <see cref="LuaChunk"/> (or any individual statement/expression) back to Lua source text -
/// the reverse of <see cref="LuaParser"/>. Formatting (tab indentation, one statement per line) doesn't
/// have to match a real Domino-generated file byte-for-byte - the engine's own Lua interpreter re-parses
/// whatever comes out, so only round-tripping to an equivalent AST matters, not exact whitespace.
/// </summary>
public static class LuaWriter
{
    public static string Write(LuaChunk chunk)
    {
        var sb = new StringBuilder();
        foreach (LuaStmt stmt in chunk.Statements)
        {
            WriteStmt(sb, stmt, 0);
        }
        return sb.ToString();
    }

    public static string WriteExpr(LuaExpr expr)
    {
        var sb = new StringBuilder();
        WriteExpr(sb, expr);
        return sb.ToString();
    }

    /// <summary>Renders one statement (with its trailing `;` and newline) at the given indent depth -
    /// the building block <see cref="Write(LuaChunk)"/> uses for a whole chunk, exposed separately so a
    /// caller assembling its own statement list (see <c>UserGraphWriter</c>'s <c>OtherStmt</c> case)
    /// doesn't have to wrap a single statement in a throwaway chunk just to render it.</summary>
    public static string WriteStmt(LuaStmt stmt, int indent = 0)
    {
        var sb = new StringBuilder();
        WriteStmt(sb, stmt, indent);
        return sb.ToString();
    }

    private static void Indent(StringBuilder sb, int level) => sb.Append('\t', level);

    private static void WriteStmt(StringBuilder sb, LuaStmt stmt, int indent)
    {
        Indent(sb, indent);
        switch (stmt)
        {
            case CommentStmt s:
                if (s.IsLong)
                {
                    sb.Append("--[[").Append(s.Text).Append("]]\n");
                }
                else
                {
                    sb.Append("--").Append(s.Text).Append('\n');
                }
                return; // comments carry their own newline; skip the shared trailer below

            case AssignStmt s:
                WriteExprList(sb, s.Targets);
                sb.Append(" = ");
                WriteExprList(sb, s.Values);
                break;

            case LocalStmt s:
                sb.Append("local ").Append(string.Join(", ", s.Names));
                if (s.Values.Count > 0)
                {
                    sb.Append(" = ");
                    WriteExprList(sb, s.Values);
                }
                break;

            case CallStmt s:
                WriteExpr(sb, s.Call);
                break;

            case FunctionDeclStmt s:
                sb.Append("function ");
                if (s.NamePath.Count == 1)
                {
                    sb.Append(s.NamePath[0]);
                }
                else
                {
                    sb.Append(string.Join('.', s.NamePath.Take(s.NamePath.Count - 1))).Append(s.IsMethod ? ':' : '.').Append(s.NamePath[^1]);
                }
                sb.Append('(').Append(string.Join(", ", s.Parameters)).Append(")\n");
                WriteBody(sb, s.Body, indent + 1);
                Indent(sb, indent);
                sb.Append("end");
                break;

            case IfStmt s:
                for (int i = 0; i < s.Clauses.Count; i++)
                {
                    if (i > 0)
                    {
                        Indent(sb, indent);
                        sb.Append("elseif ");
                    }
                    else
                    {
                        sb.Append("if ");
                    }
                    WriteExpr(sb, s.Clauses[i].Condition);
                    sb.Append(" then\n");
                    WriteBody(sb, s.Clauses[i].Body, indent + 1);
                }
                if (s.ElseBody is not null)
                {
                    Indent(sb, indent);
                    sb.Append("else\n");
                    WriteBody(sb, s.ElseBody, indent + 1);
                }
                Indent(sb, indent);
                sb.Append("end");
                break;

            case GenericForStmt s:
                sb.Append("for ").Append(string.Join(", ", s.Names)).Append(" in ");
                WriteExprList(sb, s.Iterators);
                sb.Append(" do\n");
                WriteBody(sb, s.Body, indent + 1);
                Indent(sb, indent);
                sb.Append("end");
                break;

            case NumericForStmt s:
                sb.Append("for ").Append(s.Name).Append(" = ");
                WriteExpr(sb, s.Start);
                sb.Append(", ");
                WriteExpr(sb, s.Stop);
                if (s.Step is not null)
                {
                    sb.Append(", ");
                    WriteExpr(sb, s.Step);
                }
                sb.Append(" do\n");
                WriteBody(sb, s.Body, indent + 1);
                Indent(sb, indent);
                sb.Append("end");
                break;

            case WhileStmt s:
                sb.Append("while ");
                WriteExpr(sb, s.Condition);
                sb.Append(" do\n");
                WriteBody(sb, s.Body, indent + 1);
                Indent(sb, indent);
                sb.Append("end");
                break;

            case RepeatStmt s:
                sb.Append("repeat\n");
                WriteBody(sb, s.Body, indent + 1);
                Indent(sb, indent);
                sb.Append("until ");
                WriteExpr(sb, s.Condition);
                break;

            case DoStmt s:
                sb.Append("do\n");
                WriteBody(sb, s.Body, indent + 1);
                Indent(sb, indent);
                sb.Append("end");
                break;

            case ReturnStmt s:
                sb.Append("return");
                if (s.Values.Count > 0)
                {
                    sb.Append(' ');
                    WriteExprList(sb, s.Values);
                }
                break;

            case BreakStmt:
                sb.Append("break");
                break;

            default:
                throw new NotSupportedException($"Unknown LuaStmt: {stmt.GetType().Name}");
        }
        sb.Append(";\n");
    }

    private static void WriteBody(StringBuilder sb, IReadOnlyList<LuaStmt> body, int indent)
    {
        foreach (LuaStmt stmt in body)
        {
            WriteStmt(sb, stmt, indent);
        }
    }

    private static void WriteExprList(StringBuilder sb, IReadOnlyList<LuaExpr> exprs)
    {
        for (int i = 0; i < exprs.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            WriteExpr(sb, exprs[i]);
        }
    }

    private static void WriteExpr(StringBuilder sb, LuaExpr expr)
    {
        switch (expr)
        {
            case NilExpr:
                sb.Append("nil");
                break;
            case TrueExpr:
                sb.Append("true");
                break;
            case FalseExpr:
                sb.Append("false");
                break;
            case VarargExpr:
                sb.Append("...");
                break;
            case NumberExpr e:
                sb.Append(e.Raw);
                break;
            case StringExpr e:
                sb.Append('"').Append(EscapeString(e.Value)).Append('"');
                break;
            case NameExpr e:
                sb.Append(e.Name);
                break;
            case FieldAccessExpr e:
                WriteExpr(sb, e.Target);
                sb.Append('.').Append(e.Field);
                break;
            case IndexAccessExpr e:
                WriteExpr(sb, e.Target);
                sb.Append('[');
                WriteExpr(sb, e.Key);
                sb.Append(']');
                break;
            case CallExpr e:
                WriteExpr(sb, e.Callee);
                sb.Append('(');
                WriteExprList(sb, e.Args);
                sb.Append(')');
                break;
            case MethodCallExpr e:
                WriteExpr(sb, e.Target);
                sb.Append(':').Append(e.Method).Append('(');
                WriteExprList(sb, e.Args);
                sb.Append(')');
                break;
            case UnaryExpr e:
                sb.Append(e.Op);
                if (e.Op == "not") sb.Append(' ');
                WriteExpr(sb, e.Operand);
                break;
            case BinaryExpr e:
                WriteExpr(sb, e.Left);
                sb.Append(' ').Append(e.Op).Append(' ');
                WriteExpr(sb, e.Right);
                break;
            case TableConstructorExpr e:
                sb.Append('{');
                for (int i = 0; i < e.Fields.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    WriteTableField(sb, e.Fields[i]);
                }
                sb.Append('}');
                break;
            default:
                throw new NotSupportedException($"Unknown LuaExpr: {expr.GetType().Name}");
        }
    }

    private static void WriteTableField(StringBuilder sb, TableField field)
    {
        switch (field)
        {
            case TablePositionalField f:
                WriteExpr(sb, f.Value);
                break;
            case TableNamedField f:
                sb.Append(f.Name).Append(" = ");
                WriteExpr(sb, f.Value);
                break;
            case TableKeyedField f:
                sb.Append('[');
                WriteExpr(sb, f.Key);
                sb.Append("] = ");
                WriteExpr(sb, f.Value);
                break;
            default:
                throw new NotSupportedException($"Unknown TableField: {field.GetType().Name}");
        }
    }

    private static string EscapeString(string value) => value
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n")
        .Replace("\t", "\\t")
        .Replace("\r", "\\r");
}
