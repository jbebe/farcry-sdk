using JackAll.Tools.Reach;

namespace JackAll.Tests;

/// <summary>
/// What the app reads out of the shipped verdict list: the lookup itself, and the wording it puts
/// in front of someone about to edit a dead file.
/// </summary>
public sealed class ReachListLookupTests
{
    private static ReachList Sample() => ReachList.Parse([
        "# comment",
        "",
        "AAAAAAA1\tworlds\\x\\dead.xml\tunused\tDECOY\t100\t5\tfallback:primary-present",
        "AAAAAAA2\tgraphics\\rig.hkx\tunknown\t-\t50\t0\topaque-referrers(hkx)",
        "not-a-hash\tbroken\tunused\t-\t1\t0\tunreachable",
        "AAAAAAA3\ttoo\tfew\tfields",
    ]);

    [Fact]
    public void Reads_verdict_decoy_and_reason_skipping_junk_lines()
    {
        ReachList list = Sample();

        Assert.Equal(2, list.Count);
        Assert.True(list.TryGet(0xAAAAAAA1, out ReachListEntry dead));
        Assert.Equal(ReachVerdict.Unused, dead.Verdict);
        Assert.True(dead.IsDecoy);
        Assert.Equal("fallback:primary-present", dead.Reason);
    }

    /// <summary>The distinction the whole feature rests on: `unknown` is the case the analysis
    /// refused to decide, so the app must never treat it as dead.</summary>
    [Fact]
    public void An_unknown_row_is_not_unused()
    {
        ReachList list = Sample();

        Assert.True(list.TryGet(0xAAAAAAA2, out ReachListEntry unknown));
        Assert.Equal(ReachVerdict.Unknown, unknown.Verdict);
        Assert.False(list.IsUnused(0xAAAAAAA2));
        Assert.True(list.IsUnused(0xAAAAAAA1));
    }

    [Fact]
    public void A_hash_the_list_never_mentions_is_reachable()
        => Assert.False(Sample().IsUnused(0xDEADBEEF));

    [Fact]
    public void A_missing_list_loads_empty_rather_than_throwing()
    {
        ReachList list = ReachList.Load(Path.Combine(Path.GetTempPath(), "no-such-reach-list.tsv"));

        Assert.Equal(0, list.Count);
        Assert.False(list.IsUnused(0xAAAAAAA1));
    }

    /// <summary>
    /// Every reason the shipped asset actually uses must have wording of its own. Without this, a
    /// new reason added to the analysis would silently fall through to the generic sentence and
    /// tell the user something vaguer than what is known.
    /// </summary>
    [Fact]
    public void Every_shipped_reason_has_its_own_explanation()
    {
        string path = Path.Combine(TestSupport.RepositoryRoot, "tools", "JackAll", "assets", "fc2.unused.tsv");
        string generic = ReachReasons.Explain("something nobody mapped");

        var reasons = File.ReadLines(path)
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => line.Split('\t'))
            .Where(f => f.Length >= 7 && f[2] == "unused")
            .Select(f => f[6])
            .Distinct()
            .ToList();

        Assert.NotEmpty(reasons);
        foreach (string reason in reasons)
        {
            string explained = ReachReasons.Explain(reason);
            Assert.False(string.IsNullOrWhiteSpace(explained), reason);

            // "unreachable" is the one reason the generic sentence is the right answer for: it
            // means exactly "nothing points at it", with nothing more specific to say.
            if (reason != "unreachable")
            {
                Assert.True(explained != generic, $"'{reason}' falls through to the generic explanation.");
            }
        }
    }
}
