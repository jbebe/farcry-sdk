using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace Domino.Core;

/// <summary>The one place that turns Domino Lua source text into a syntax tree - every parser in this
/// project (<see cref="Nodes.ReflectionBoxParser"/>, <see cref="Graphs.UserGraphParser"/>) starts here,
/// so the dialect options and error-reporting behavior are chosen exactly once.</summary>
public static class DominoLuaSource
{
    /// <summary>The permissive superset preset - Domino's own dialect is plain, boring Lua (nothing
    /// version-specific; see the project's own notes on why the "Lua 4.1" finding turned out to be
    /// irrelevant at the source level), so there's no real dialect choice to make here. `.All` just
    /// means a stray extension somewhere in the real corpus doesn't turn into a false parse failure.</summary>
    private static readonly LuaParseOptions Options = new(LuaSyntaxOptions.All);

    /// <summary>Parses <paramref name="source"/>, throwing <see cref="FormatException"/> if it doesn't
    /// parse cleanly - Loretta's own parser is error-tolerant (it always produces a tree, with errors
    /// reported as diagnostics rather than exceptions), but every caller here wants "this file is
    /// malformed" to surface as a thrown exception, matching how the rest of this project's batch
    /// commands and tests detect failures.</summary>
    public static CompilationUnitSyntax Parse(string source)
    {
        SyntaxTree tree = LuaSyntaxTree.ParseText(source, Options);
        var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new FormatException(string.Join("; ", errors.Select(d => d.ToString())));
        }
        return (CompilationUnitSyntax)tree.GetRoot();
    }
}
