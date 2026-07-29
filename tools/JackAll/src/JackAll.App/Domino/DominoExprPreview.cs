using Domino.Core.Lua;

namespace JackAll.App.Domino;

/// <summary>A short, single-line rendering of a parameter value for a node box label - not a real
/// pretty-printer (see the Domino.Cli diagnostic tool for that), just enough to distinguish
/// "Entity: 205..." from "Command: BuddyUnlock" at a glance.</summary>
internal static class DominoExprPreview
{
    public static string Short(LuaExpr expr) => Truncate(expr switch
    {
        StringExpr e => $"\"{e.Value}\"",
        NumberExpr e => e.Raw,
        NameExpr e => e.Name,
        NilExpr => "nil",
        TrueExpr => "true",
        FalseExpr => "false",
        FieldAccessExpr e => $"{Short(e.Target)}.{e.Field}",
        _ => expr.GetType().Name,
    });

    private static string Truncate(string s) => s.Length > 40 ? s[..37] + "..." : s;
}
