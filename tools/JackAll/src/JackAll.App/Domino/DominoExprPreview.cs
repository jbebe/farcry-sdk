using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.App.Domino;

/// <summary>A short, single-line rendering of a parameter value for a node box label - not a real
/// pretty-printer, just enough to distinguish "Entity: 205..." from "Command: BuddyUnlock" at a
/// glance.</summary>
internal static class DominoExprPreview
{
    public static string Short(ExpressionSyntax expr) => Truncate(expr switch
    {
        LiteralExpressionSyntax lit when lit.Kind() == SyntaxKind.StringLiteralExpression => $"\"{lit.Token.ValueText}\"",
        LiteralExpressionSyntax lit => lit.Token.Text, // number / nil / true / false - raw token text reads fine as-is
        IdentifierNameSyntax e => e.Name,
        MemberAccessExpressionSyntax e => $"{Short(e.Expression)}.{e.MemberName.Text}",
        _ => expr.GetType().Name,
    });

    private static string Truncate(string s) => s.Length > 40 ? s[..37] + "..." : s;
}
