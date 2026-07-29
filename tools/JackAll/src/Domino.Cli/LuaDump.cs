using Domino.Core.Lua;

namespace Domino.Cli;

/// <summary>Minimal, lossy pretty-printer for <see cref="LuaStmt"/>/<see cref="LuaExpr"/> — CLI-diagnostic
/// use only (e.g. showing an unclassified <c>OtherStmt</c> in something readable), not a real code
/// generator. The real write-path generator (Phase 4) is a separate, exactness-focused component.</summary>
public static class LuaDump
{
    public static string Stmt(LuaStmt stmt) => stmt switch
    {
        AssignStmt s => $"{string.Join(", ", s.Targets.Select(Expr))} = {string.Join(", ", s.Values.Select(Expr))}",
        LocalStmt s => $"local {string.Join(", ", s.Names)}" + (s.Values.Count > 0 ? $" = {string.Join(", ", s.Values.Select(Expr))}" : ""),
        CallStmt s => Expr(s.Call),
        FunctionDeclStmt s => $"function {string.Join(s.IsMethod ? ":" : ".", s.NamePath)}({string.Join(", ", s.Parameters)}) ... end",
        IfStmt => "if ... end",
        GenericForStmt s => $"for {string.Join(", ", s.Names)} in {string.Join(", ", s.Iterators.Select(Expr))} do ... end",
        NumericForStmt s => $"for {s.Name} = {Expr(s.Start)}, {Expr(s.Stop)} do ... end",
        WhileStmt => "while ... do ... end",
        RepeatStmt => "repeat ... until ...",
        DoStmt => "do ... end",
        ReturnStmt s => $"return {string.Join(", ", s.Values.Select(Expr))}",
        BreakStmt => "break",
        CommentStmt s => $"--{s.Text}",
        _ => stmt.GetType().Name,
    };

    public static string Expr(LuaExpr expr) => expr switch
    {
        NilExpr => "nil",
        TrueExpr => "true",
        FalseExpr => "false",
        VarargExpr => "...",
        NumberExpr e => e.Raw,
        StringExpr e => $"\"{e.Value}\"",
        NameExpr e => e.Name,
        FieldAccessExpr e => $"{Expr(e.Target)}.{e.Field}",
        IndexAccessExpr e => $"{Expr(e.Target)}[{Expr(e.Key)}]",
        CallExpr e => $"{Expr(e.Callee)}({string.Join(", ", e.Args.Select(Expr))})",
        MethodCallExpr e => $"{Expr(e.Target)}:{e.Method}({string.Join(", ", e.Args.Select(Expr))})",
        UnaryExpr e => $"{e.Op}{Expr(e.Operand)}",
        BinaryExpr e => $"{Expr(e.Left)} {e.Op} {Expr(e.Right)}",
        TableConstructorExpr => "{...}",
        _ => expr.GetType().Name,
    };
}
