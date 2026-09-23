using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;
using Loretta.CodeAnalysis.Lua.Syntax;

namespace JackAll.Tools.Domino;

/// <summary>Everything known to be wrong with one user graph, the graph it was checked as, and the debug
/// twin it was checked against.</summary>
public sealed record DominoCheckResult(ReconstructedGraph? Graph, DominoDebugTwin? Twin, IReadOnlyList<DominoFinding> Findings);

/// <summary>
/// Checks one `user\` graph end to end: that it parses, that the writer reproduces it (byte for byte,
/// and token for token in BlackBox's own form, which proves no statement shape is lost), that its
/// reconstruction agrees with its debug twin, and <see cref="DominoLint"/>'s rules.
/// </summary>
public static class DominoCheck
{
    public static DominoCheckResult Run(string source, string? twinSource, DominoNodeCatalog? catalog)
    {
        CompilationUnitSyntax root;
        try
        {
            root = DominoLuaSource.Parse(source);
        }
        catch (FormatException ex)
        {
            return new DominoCheckResult(null, null, [new DominoFinding(LintSeverity.Error, "parse", ex.Message)]);
        }

        UserGraph graph = UserGraphParser.Parse(root);
        var findings = new List<DominoFinding>();
        if (UserGraphWriter.Write(graph) != source)
        {
            findings.Add(new DominoFinding(LintSeverity.Error, "round-trip", "writing the file back does not reproduce it"));
        }
        if (!SameTokens(root, UserGraphWriter.WriteCanonical(graph)))
        {
            findings.Add(new DominoFinding(LintSeverity.Warning, "non-blackbox-shape",
                "the file holds a statement shape BlackBox does not write"));
        }

        DominoDebugTwin? twin = null;
        if (twinSource is not null)
        {
            try
            {
                twin = DominoDebugTwin.FromGraph(UserGraphParser.Parse(DominoLuaSource.Parse(twinSource)));
            }
            catch (FormatException ex)
            {
                findings.Add(new DominoFinding(LintSeverity.Warning, "unparseable-twin", $"the debug twin does not parse: {ex.Message}"));
            }
        }

        ReconstructedGraph reconstructed = GraphBuilder.Build(graph, catalog, twin);
        findings.AddRange(DominoLint.Run(reconstructed));
        return new DominoCheckResult(reconstructed, twin, findings);
    }

    /// <summary>The findings an edited graph has that the version it replaces did not, so a mod is told
    /// only about what it introduced.</summary>
    public static IReadOnlyList<DominoFinding> NewSince(DominoCheckResult edited, DominoCheckResult? original)
    {
        if (original is null)
        {
            return edited.Findings;
        }
        var before = original.Findings.Select(Key).ToHashSet();
        return edited.Findings.Where(f => !before.Contains(Key(f))).ToList();

        static (string, string?, string?, string) Key(DominoFinding f) => (f.Rule, f.Function, f.NodeId, f.Message);
    }

    private static bool SameTokens(CompilationUnitSyntax source, string canonical)
    {
        try
        {
            return source.DescendantTokens().Select(t => t.Text)
                .SequenceEqual(DominoLuaSource.Parse(canonical).DescendantTokens().Select(t => t.Text), StringComparer.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
