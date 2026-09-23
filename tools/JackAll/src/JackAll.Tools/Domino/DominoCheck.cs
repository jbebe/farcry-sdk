using JackAll.Tools.Domino.Graphs;
using JackAll.Tools.Domino.Nodes;
using JackAll.Tools.World;

namespace JackAll.Tools.Domino;

/// <summary>Everything known to be wrong with one user graph, and the graph it was checked as.</summary>
public sealed record DominoCheckResult(ReconstructedGraph? Graph, IReadOnlyList<DominoFinding> Findings)
{
    public bool HasErrors => Findings.Any(f => f.Severity == LintSeverity.Error);
}

/// <summary>
/// Checks one `user\` graph end to end: that it parses, that the writer reproduces it (byte for byte,
/// and token for token in BlackBox's own form, which proves no statement shape is lost), that its
/// reconstruction agrees with its debug twin, and <see cref="DominoLint"/>'s rules.
/// </summary>
public static class DominoCheck
{
    public static DominoCheckResult Run(string source, string? twinSource, DominoNodeCatalog? catalog)
    {
        UserGraph graph;
        try
        {
            graph = UserGraphParser.Parse(DominoLuaSource.Parse(source));
        }
        catch (FormatException ex)
        {
            return new DominoCheckResult(null, [new DominoFinding(LintSeverity.Error, "parse", ex.Message)]);
        }

        var findings = new List<DominoFinding>();
        if (UserGraphWriter.Write(graph) != source)
        {
            findings.Add(new DominoFinding(LintSeverity.Error, "round-trip", "writing the file back does not reproduce it"));
        }
        if (!SameTokens(source, UserGraphWriter.WriteCanonical(graph)))
        {
            findings.Add(new DominoFinding(LintSeverity.Warning, "unrepresented",
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
                findings.Add(new DominoFinding(LintSeverity.Warning, "stale-twin", $"the debug twin does not parse: {ex.Message}"));
            }
        }

        ReconstructedGraph reconstructed = GraphBuilder.Build(graph, catalog, twin);
        findings.AddRange(DominoLint.Run(reconstructed));
        return new DominoCheckResult(reconstructed, findings);
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

    private static bool SameTokens(string a, string b)
    {
        try
        {
            return DominoLuaSource.Parse(a).DescendantTokens().Select(t => t.Text)
                .SequenceEqual(DominoLuaSource.Parse(b).DescendantTokens().Select(t => t.Text), StringComparer.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
