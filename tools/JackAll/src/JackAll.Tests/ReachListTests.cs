using JackAll.Tools.Reach;

namespace JackAll.Tests;

/// <summary>
/// The checked-in verdict list (<c>assets/fc2.unused.tsv</c>) against what the docs already
/// establish about the retail install. Runs without a corpus: the asset is the artifact under
/// test, so a regenerated list that lost a known-dead file - or gained a known-live one - fails
/// here rather than quietly shipping.
/// </summary>
public sealed class ReachListTests
{
    private sealed record Row(string Path, string Verdict, string Flags, long Bytes, int OutRefs, string Reason);

    private static readonly Lazy<IReadOnlyList<Row>> Rows = new(() =>
    {
        string path = Path.Combine(TestSupport.RepositoryRoot, "tools", "JackAll", "assets", "fc2.unused.tsv");
        return [.. File.ReadLines(path)
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Split('\t'))
            .Select(f => new Row(f[1], f[2], f[3], long.Parse(f[4]), int.Parse(f[5]), f[6]))];
    });

    private static IEnumerable<Row> Matching(Func<Row, bool> predicate) => Rows.Value.Where(predicate);

    private static Row Single(string path)
        => Assert.Single(Matching(r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)));

    [Fact]
    public void The_list_holds_only_unused_and_unknown_rows()
    {
        Assert.NotEmpty(Rows.Value);
        Assert.All(Rows.Value, r => Assert.Contains(r.Verdict, (string[])["unused", "unknown"]));
    }

    [Fact]
    public void The_authoring_twins_the_engine_never_loads_are_listed()
    {
        Assert.Equal("unused", Single(@"graphics\move\movemgrnamed.bin").Verdict);
        Assert.Equal("unused", Single(@"graphics\move\dlc1named.bin").Verdict);

        // Every Domino graph ships twice; the .debug.lua twin is a topology oracle.
        Assert.All(Matching(r => r.Path.EndsWith(".debug.lua", StringComparison.OrdinalIgnoreCase)),
            r => Assert.Equal("unused", r.Verdict));
        Assert.True(Matching(r => r.Path.EndsWith(".debug.lua", StringComparison.OrdinalIgnoreCase)).Count() > 400);
    }

    [Fact]
    public void The_readable_depload_twins_are_listed_as_fallbacks_and_flagged_as_decoys()
    {
        var twins = Matching(r => r.Path.EndsWith("_depload.xml", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(twins);
        Assert.All(twins, r =>
        {
            Assert.Equal("unused", r.Verdict);
            Assert.StartsWith("fallback", r.Reason);
            Assert.Contains("DECOY", r.Flags);
        });
    }

    /// <summary>The point of the decoy flag: the biggest dead files must be the ones it surfaces.</summary>
    [Fact]
    public void The_largest_dead_files_are_all_flagged_as_decoys()
    {
        var biggest = Matching(r => r.Verdict == "unused").OrderByDescending(r => r.Bytes).Take(25);

        Assert.All(biggest, r => Assert.Contains("DECOY", r.Flags));
        Assert.Contains(biggest, r => r.Path.EndsWith(@"\entitylibrary_full.fcb", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Console_leftovers_are_listed_as_platform_bound_not_merely_unreferenced()
    {
        var console = Matching(r => ReachPolicy.IsConsoleOnly(r.Path)).ToList();

        Assert.NotEmpty(console);
        Assert.All(console, r => Assert.Equal("console-only", r.Reason));
    }

    [Fact]
    public void Files_the_game_demonstrably_loads_are_absent()
    {
        // One per reachability mechanism: a hardcoded literal, a MOVE clip, a composed sector
        // name, and the patch override the whole entity system hangs off.
        string[] live =
        [
            @"config\alwaysloaded.xml",
            @"graphics\move\movemgr.bin",
            @"generated\entitylibrarypatchoverride.fcb",
            @"levels\w1_b_2\generated\worldsectors\worldsector4210.data.fcb",
        ];

        Assert.All(live, path => Assert.DoesNotContain(Rows.Value,
            r => string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>An unnamed entry can never be proven dead - nothing can spell its path - so the
    /// ~52,700 of them must all be <c>unknown</c> if they appear at all.</summary>
    [Fact]
    public void Unnamed_entries_are_never_called_unused()
        => Assert.All(Matching(r => r.Path.StartsWith("_unknown\\", StringComparison.OrdinalIgnoreCase)),
            r => Assert.Equal("unknown", r.Verdict));
}
