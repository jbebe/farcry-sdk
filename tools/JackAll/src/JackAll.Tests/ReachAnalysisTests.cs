using JackAll.Core.Xrefs;
using JackAll.Tools.Reach;

namespace JackAll.Tests;

/// <summary>
/// The reachability engine over a synthetic graph - the rules under test are about which edges
/// carry which flags, and that is not a fact about any particular install.
/// </summary>
public sealed class ReachAnalysisTests
{
    private const uint Root = 0x100;
    private const uint Texture = 0x200;
    private const uint Mip = 0x300;
    private const uint Manifest = 0x400;
    private const uint Parent = 0x500;
    private const uint Child = 0x600;
    private const uint Orphan = 0x700;

    private static ReachFile FileFor(uint hash, string path, string ext = "xbt",
        long size = 10, string source = "worlds", bool named = true)
        => new(hash, path, ext, size, source, named);

    private static ReferenceGraph GraphOf(params RefEdge[] edges)
        => new(
            ReferenceIndex.Build(edges, [], new Dictionary<uint, string>(), edges.Select(e => e.SourceFile).Distinct()),
            new ReferenceHarvest([], [], new Dictionary<uint, string>(), [], []));

    private static EngineRoots RootsOf(params string[] lines) => EngineRoots.Parse(lines);

    private static IReadOnlyDictionary<uint, ReachRow> Run(
        IReadOnlyList<ReachFile> corpus, ReferenceGraph graph, EngineRoots roots)
        => ReachAnalysis.Run(corpus, graph, roots).Rows.ToDictionary(r => r.File.Hash);

    [Fact]
    public void Flags_propagate_through_file_edges_to_a_fixpoint()
    {
        var graph = GraphOf(
            new RefEdge(Root, RefSpace.FilePath, Texture, RefKind.FcbPathValue, 0, 0),
            new RefEdge(Texture, RefSpace.FilePath, Mip, RefKind.XbtMipCompanion, 0, 0));
        var roots = RootsOf("literal\tSP\ta\\root.fcb");
        var rows = Run(
            [FileFor(Root, "a\\root.fcb", "fcb"), FileFor(Texture, "a\\t.xbt"), FileFor(Mip, "a\\t_mip0.xbt")],
            graph, roots);

        Assert.Equal(ReachVerdict.UsedSpOnly, rows[Root].Verdict);
        Assert.Equal(ReachVerdict.UsedSpOnly, rows[Mip].Verdict);
        Assert.StartsWith("via:XbtMipCompanion:", rows[Mip].Reason);
    }

    [Fact]
    public void Sp_and_mp_roots_meeting_at_one_file_make_it_plain_used()
    {
        const uint mpRoot = 0x101;
        var graph = GraphOf(
            new RefEdge(Root, RefSpace.FilePath, Texture, RefKind.TextPath, 0, 0),
            new RefEdge(mpRoot, RefSpace.FilePath, Texture, RefKind.TextPath, 0, 0));
        var roots = RootsOf("literal\tSP\ta\\sp.xml", "literal\tMP\ta\\mp.xml");
        var rows = Run(
            [FileFor(Root, "a\\sp.xml", "xml"), FileFor(mpRoot, "a\\mp.xml", "xml"), FileFor(Texture, "a\\t.xbt")],
            graph, roots);

        Assert.Equal(ReachVerdict.UsedSpOnly, rows[Root].Verdict);
        Assert.Equal(ReachVerdict.UsedMpOnly, rows[mpRoot].Verdict);
        Assert.Equal(ReachVerdict.Used, rows[Texture].Verdict);
    }

    [Fact]
    public void Editor_only_reach_is_used_with_the_editor_flag()
    {
        var graph = GraphOf(new RefEdge(Root, RefSpace.FilePath, Texture, RefKind.TextPath, 0, 0));
        var roots = RootsOf("literal\tEDITOR\ta\\ige.xml");
        var rows = Run([FileFor(Root, "a\\ige.xml", "xml"), FileFor(Texture, "a\\t.xbt")], graph, roots);

        Assert.Equal(ReachVerdict.Used, rows[Texture].Verdict);
        Assert.Equal(ReachFlags.Editor, rows[Texture].Flags);
    }

    [Fact]
    public void A_depload_child_needs_both_its_manifest_and_its_parent()
    {
        var edges = new[]
        {
            new RefEdge(Manifest, RefSpace.FilePath, Child, RefKind.DepLoadDependency, Parent, 0),
        };
        ReachFile[] corpus =
        [
            FileFor(Manifest, "worlds\\w\\x_depload.dat", "dat"),
            FileFor(Parent, "a\\parent.xbg", "xbg"),
            FileFor(Child, "a\\child.mab", "mab"),
        ];

        // Manifest reachable, parent not: the child must stay dark.
        var manifestOnly = Run(corpus, GraphOf(edges),
            RootsOf("literal\tSP\tworlds\\w\\x_depload.dat"));
        Assert.NotEqual(ReachVerdict.UsedSpOnly, manifestOnly[Child].Verdict);

        // Both reachable: the child inherits the manifest's flags, not the parent's.
        var both = Run(corpus, GraphOf(edges),
            RootsOf("literal\tSP\tworlds\\w\\x_depload.dat", "literal\tGLOBAL\ta\\parent.xbg"));
        Assert.Equal(ReachVerdict.UsedSpOnly, both[Child].Verdict);
    }

    [Fact]
    public void The_name_probe_reaches_a_file_whose_hash_hides_in_a_name_edge()
    {
        var graph = GraphOf(new RefEdge(Root, RefSpace.EngineName, Texture, RefKind.FcbNameValue, 0, 0));
        var roots = RootsOf("literal\tGLOBAL\ta\\root.fcb");
        ReachResult result = ReachAnalysis.Run(
            [FileFor(Root, "a\\root.fcb", "fcb"), FileFor(Texture, "a\\t.xbt")], graph, roots);

        Assert.Equal(1, result.NameProbeMatches);
        Assert.Equal(ReachVerdict.Used, result.Rows.Single(r => r.File.Hash == Texture).Verdict);
    }

    [Fact]
    public void An_empty_graph_refuses_to_classify_anything()
        => Assert.Throws<InvalidOperationException>(() => ReachAnalysis.Run(
            [FileFor(Root, "a\\root.fcb", "fcb")],
            new ReferenceGraph(ReferenceIndex.Empty, new ReferenceHarvest([], [], new Dictionary<uint, string>(), [], [])),
            RootsOf("literal\tGLOBAL\ta\\root.fcb")));

    [Fact]
    public void Conservative_verdicts_for_the_unreached()
    {
        var graph = GraphOf(new RefEdge(Root, RefSpace.FilePath, Texture, RefKind.TextPath, 0, 0));
        var roots = RootsOf("literal\tGLOBAL\ta\\root.xml");
        var rows = Run(
            [
                FileFor(Root, "a\\root.xml", "xml"),
                FileFor(Texture, "a\\t.xbt"),
                FileFor(Orphan, "a\\dead.xbt"),
                FileFor(0x701, "_unknown\\misc\\00000701.bin", "bin", named: false),
                FileFor(0x702, "a\\rig.hkx", "hkx"),
                FileFor(0x4A724578, "levels\\ige_map\\generated\\sdat\\sd10_shadow.xbt"),
                FileFor(0x703, "config\\presets\\xenon\\video.xml", "xml"),
            ],
            graph, roots);

        Assert.Equal(ReachVerdict.Unused, rows[Orphan].Verdict);
        Assert.Equal("unreachable", rows[Orphan].Reason);
        Assert.Equal(ReachVerdict.Unknown, rows[0x701].Verdict);
        Assert.Equal("unnamed", rows[0x701].Reason);
        Assert.Equal(ReachVerdict.Unknown, rows[0x702].Verdict);
        Assert.Equal("opaque-referrers(hkx)", rows[0x702].Reason);
        Assert.NotEqual(ReachVerdict.Unused, rows[0x4A724578].Verdict);
        Assert.StartsWith("collision:", rows[0x4A724578].Reason);
        Assert.Equal(ReachVerdict.Unused, rows[0x703].Verdict);
        Assert.Equal("console-only", rows[0x703].Reason);
    }

    [Fact]
    public void An_unused_manifest_shaped_file_is_flagged_as_a_decoy()
    {
        // Its own outgoing edges exist, but nothing reaches IT - the world1_depload.xml shape.
        var edges = Enumerable.Range(0, ReachPolicy.DecoyOutRefs)
            .Select(i => new RefEdge(Orphan, RefSpace.FilePath, (uint)(0x1000 + i), RefKind.TextPath, 0, 0))
            .ToArray();
        var rows = Run(
            [FileFor(Orphan, "worlds\\w\\w_depload.xml", "xml", size: 100)],
            GraphOf(edges),
            RootsOf("fallback\tNONE\t^worlds\\\\.*_depload\\.xml$\tfallback:primary-present"));

        Assert.Equal(ReachVerdict.Unused, rows[Orphan].Verdict);
        Assert.Equal("fallback:primary-present", rows[Orphan].Reason);
        Assert.True(rows[Orphan].Flags.HasFlag(ReachFlags.Decoy));
    }

    [Fact]
    public void A_world_pattern_takes_its_flags_from_the_world_rules()
    {
        // An unrelated edge only - the tmpla row must stay dark on its own account.
        var graph = GraphOf(new RefEdge(0x999, RefSpace.FilePath, 0x998, RefKind.TextPath, 0, 0));
        var roots = RootsOf(
            "world\tSP\tworld1",
            "world\tNONE\ttmpla\tdev-leftover(-benchmark)",
            "pattern\tWORLD\t^worlds\\\\(?<world>[^\\\\]+)\\\\mapcompass\\.xbt$");
        var rows = Run(
            [
                FileFor(Root, "worlds\\world1\\mapcompass.xbt"),
                FileFor(Texture, "worlds\\tmpla\\mapcompass.xbt"),
            ],
            graph, roots);

        Assert.Equal(ReachVerdict.UsedSpOnly, rows[Root].Verdict);
        Assert.Equal(ReachVerdict.Unused, rows[Texture].Verdict);
        Assert.Equal("dev-leftover(-benchmark)", rows[Texture].Reason);
    }
}
