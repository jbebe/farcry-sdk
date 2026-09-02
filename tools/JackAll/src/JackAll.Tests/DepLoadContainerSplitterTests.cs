using System.Collections.Concurrent;
using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tests;

/// <summary>
/// `depload.dat` as a splitting container: one fragment per parent, so a mod declares the one
/// dependency list it cares about instead of shipping a 220 KB manifest. The gate is the same shape
/// the `.fcb` splitter has - a real shipped file, taken apart and put back together unchanged.
/// </summary>
public class DepLoadContainerSplitterTests : IDisposable
{
    private const string Container = @"worlds\world1\generated\world1_depload.dat";
    private const uint Animation = 0xB0604725;
    private const uint Dragunov = 3882209901;
    private const uint DartRifle = 115510436;

    private readonly string _sandbox;
    private readonly DepLoadContainerSplitter _splitter = new();

    public DepLoadContainerSplitterTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), "fc2mm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, recursive: true); } catch { /* best effort */ }
    }

    public static TheoryData<string> CorpusFiles() => DepLoadDocumentTests.CorpusFiles();

    [Theory]
    [InlineData("world1_depload.dat", true)]
    [InlineData("entitylibrary.fcb", true)]
    [InlineData("patch.dat", false)]
    [InlineData("common.dat", false)]
    [InlineData("movemgr.bin", false)]
    public void Only_a_depload_is_a_container_not_every_dat(string fileName, bool expected)
        => Assert.Equal(expected, ContainerFormats.IsContainerSegment(fileName));

    /// <summary>A depload fragment addresses like any other: container path, then the fragment id.</summary>
    [Fact]
    public void A_staged_depload_fragment_classifies_against_its_container()
    {
        string root = Path.Combine(_sandbox, "layer");
        string dir = Path.Combine(root, "mods", @"worlds\world1\generated\world1_depload.dat");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "3882209901.xml"), "<Resource crc_ID=\"3882209901\" />");

        var layer = new FolderModLayer(root, "layer");

        FragmentOverride staged = Assert.Single(layer.FragmentOverrides[NameHash.Compute(Container)]);
        Assert.Equal("3882209901.xml", staged.FragmentId, ignoreCase: true);
        Assert.Empty(layer.Hashes);
    }

    /// <summary>
    /// Taking every parent out as a fragment and putting them all back has to reproduce the file -
    /// the same round trip the binary codec is gated on, one level up.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Every_parent_extracts_and_splices_back_unchanged(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        IContainerTree tree = _splitter.Open(original);

        Dictionary<string, string> everyFragment = DepLoadDocument.Decode(original).Parents
            .ToDictionary(p => DepLoadContainerSplitter.IdOf(p.Hash),
                p => tree.Extract(DepLoadContainerSplitter.IdOf(p.Hash))!);
        byte[] rebuilt = _splitter.Apply(original, everyFragment);

        int at = Fc2Corpus.FirstDifference(original, rebuilt);
        Assert.True(at < 0, Fc2Corpus.DescribeDifference(path, original, rebuilt));
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Splicing_one_fragment_leaves_every_other_parent_alone(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        DepLoadFile before = DepLoadDocument.Decode(original);
        if (before.Parents.All(p => p.Hash != Dragunov)) return;

        DepLoadParent edited = before.Parents.First(p => p.Hash == Dragunov);
        edited = edited with { Children = [.. edited.Children, new DepLoadChild(0x11641D75, Animation)] };

        DepLoadFile after = DepLoadDocument.Decode(_splitter.Apply(
            original, new Dictionary<string, string> { ["3882209901.xml"] = DepLoadXml.FragmentToXml(edited) }));

        Assert.Empty(DepLoadValidate.Problems(after));
        foreach (DepLoadParent parent in after.Parents.Where(p => p.Hash != Dragunov))
        {
            Assert.Equal(
                before.Parents.First(p => p.Hash == parent.Hash).Children, parent.Children);
        }
        Assert.Contains(after.Parents.First(p => p.Hash == Dragunov).Children,
            c => c.Hash == 0x11641D75);
    }

    [Fact]
    public void A_fragment_for_a_parent_the_container_lacks_is_added_in_sorted_position()
    {
        byte[] container = DepLoadDocument.Encode(new DepLoadFile([
            new DepLoadParent(0x10, 0, [new DepLoadChild(0xA1, Animation)]),
            new DepLoadParent(0x30, 1, [new DepLoadChild(0xA2, Animation)]),
        ]));

        var added = new DepLoadParent(0x20, 0, [new DepLoadChild(0xA3, Animation)]);
        DepLoadFile result = DepLoadDocument.Decode(_splitter.Apply(
            container, new Dictionary<string, string> { ["32.xml"] = DepLoadXml.FragmentToXml(added) }));

        Assert.Equal([0x10u, 0x20u, 0x30u], result.Parents.Select(p => p.Hash));
        Assert.Empty(DepLoadValidate.Problems(result));
    }

    /// <summary>A fragment whose filename names a different resource than its content is a rename
    /// gone wrong, and would silently override the wrong parent.</summary>
    [Fact]
    public void A_fragment_filed_under_the_wrong_id_is_refused()
    {
        byte[] container = DepLoadDocument.Encode(new DepLoadFile([
            new DepLoadParent(0x10, 0, [new DepLoadChild(0xA1, Animation)]),
        ]));
        var parent = new DepLoadParent(0x10, 0, []);

        Assert.Throws<InvalidDataException>(() => _splitter.Apply(
            container, new Dictionary<string, string> { ["3735928559.xml"] = DepLoadXml.FragmentToXml(parent) }));
    }

    /// <summary>
    /// A label ahead of the number is the author's to choose and the number is what binds, so a
    /// staged file can be renamed - or named differently by two mods - without orphaning anything.
    /// This is the scheme a placed entity's fragment already uses, so the shared id comparer needs
    /// no special case for it.
    /// </summary>
    [Theory]
    [InlineData("3882209901.xml")]
    [InlineData("dragunov.3882209901.xml")]
    [InlineData("DRAGUNOV.3882209901.xml")]
    [InlineData("something_else_entirely.3882209901.xml")]
    public void A_label_in_front_of_the_number_names_the_same_fragment(string id)
    {
        byte[] container = DepLoadDocument.Encode(new DepLoadFile([
            new DepLoadParent(Dragunov, 0, [new DepLoadChild(0xA1, Animation)]),
        ]));
        var edited = new DepLoadParent(Dragunov, 0, [new DepLoadChild(0xA1, Animation), new DepLoadChild(0xAAAA, Animation)]);

        Assert.NotNull(_splitter.Open(container).Extract(id));
        Assert.True(FcbFragments.IdComparer.Equals(id, "3882209901.xml"));

        DepLoadFile applied = DepLoadDocument.Decode(_splitter.Apply(
            container, new Dictionary<string, string> { [id] = DepLoadXml.FragmentToXml(edited) }));
        Assert.Contains(applied.Parents.Single().Children, c => c.Hash == 0xAAAA);
    }

    /// <summary>The label is what a caller already knows; nothing resolves a hash back to a name.</summary>
    [Fact]
    public void A_named_id_reads_by_its_name_and_binds_by_its_number()
    {
        Assert.Equal("dragunov.3882209901.xml", DepLoadContainerSplitter.IdOf(Dragunov, "dragunov"));
        Assert.Equal("3882209901.xml", DepLoadContainerSplitter.IdOf(Dragunov));
        Assert.Equal("dragunov.xbg.3882209901.xml",
            DepLoadContainerSplitter.IdOf(Dragunov, @"graphics\weapons\special\dragunov.xbg"));
    }

    /// <summary>
    /// A fragment carries no <c>childIndex</c>. It is a whole-file layout detail that shifts whenever
    /// anything earlier changes, so carrying it would make fragments churn against unrelated edits.
    /// </summary>
    [Fact]
    public void A_fragment_carries_no_block_order()
    {
        string xml = DepLoadXml.FragmentToXml(new DepLoadParent(0x10, 4321, [new DepLoadChild(0xA1, Animation)]));

        Assert.DoesNotContain("childIndex", xml, StringComparison.Ordinal);
        Assert.Contains("CAnimationResource", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two mods registering clips under *different* packages compose, because they are different
    /// fragments and never meet.
    /// </summary>
    [Fact]
    public void Two_mods_registering_under_different_packages_both_survive()
    {
        byte[] container = DepLoadDocument.Encode(new DepLoadFile([
            new DepLoadParent(Dragunov, 0, [new DepLoadChild(0xA1, Animation)]),
            new DepLoadParent(DartRifle, 1, [new DepLoadChild(0xA2, Animation)]),
        ]));

        FolderModLayer modA = MakeLayer("mod_a",
            new DepLoadParent(Dragunov, 0, [new DepLoadChild(0xA1, Animation), new DepLoadChild(0xAAAA, Animation)]));
        FolderModLayer modB = MakeLayer("mod_b",
            new DepLoadParent(DartRifle, 0, [new DepLoadChild(0xA2, Animation), new DepLoadChild(0xBBBB, Animation)]));

        DepLoadFile merged = DepLoadDocument.Decode(_splitter.Apply(container, Resolve(container, modA, modB)));

        Assert.Contains(merged.Parents.Single(p => p.Hash == Dragunov).Children, c => c.Hash == 0xAAAA);
        Assert.Contains(merged.Parents.Single(p => p.Hash == DartRifle).Children, c => c.Hash == 0xBBBB);
        Assert.Empty(DepLoadValidate.Problems(merged));
    }

    /// <summary>
    /// Two mods appending to the *same* package do **not** merge, and this pins that rather than
    /// wishing otherwise. Diff3 is line-based and both edits insert at the same place, so it is a
    /// genuine textual conflict. It could be made to merge by canonicalizing children into hash
    /// order, but 30% of shipped parents store them in some other order, and trading that fidelity
    /// for a merge convenience is not worth it while the meaning of the order is unknown.
    ///
    /// The saving grace is that a build reports the collision instead of swallowing it - unlike a
    /// whole-file override, where the later mod wins in silence.
    /// </summary>
    [Fact]
    public void Two_mods_appending_to_one_package_collide_loudly()
    {
        var vanilla = new DepLoadParent(Dragunov, 0, [new DepLoadChild(0xA1, Animation)]);
        byte[] container = DepLoadDocument.Encode(new DepLoadFile([vanilla]));

        FolderModLayer modA = MakeLayer("mod_a",
            vanilla with { Children = [.. vanilla.Children, new DepLoadChild(0xAAAA, Animation)] });
        FolderModLayer modB = MakeLayer("mod_b",
            vanilla with { Children = [.. vanilla.Children, new DepLoadChild(0xBBBB, Animation)] });

        Assert.Throws<InvalidDataException>(() => Resolve(container, modA, modB));

        // What `mod build` actually does: load order wins, and the collision is reported.
        var conflicts = new ConcurrentQueue<FragmentConflict>();
        Dictionary<string, string> resolved = Resolve(container, conflicts, modA, modB);
        DepLoadFile merged = DepLoadDocument.Decode(_splitter.Apply(container, resolved));

        FragmentConflict reported = Assert.Single(conflicts);
        Assert.Equal("mod_b", reported.WinningLayer);
        IReadOnlyList<DepLoadChild> children = merged.Parents.Single(p => p.Hash == Dragunov).Children;
        Assert.Contains(children, c => c.Hash == 0xBBBB);
        Assert.DoesNotContain(children, c => c.Hash == 0xAAAA);
    }

    private Dictionary<string, string> Resolve(byte[] container, params FolderModLayer[] layers)
        => Resolve(container, null, layers);

    private Dictionary<string, string> Resolve(
        byte[] container, ConcurrentQueue<FragmentConflict>? conflicts, params FolderModLayer[] layers)
    {
        IContainerTree tree = _splitter.Open(container);
        return FragmentMerge.BuildOverrideIndex(layers)[NameHash.Compute(Container)]
            .ToDictionary(kv => kv.Key, kv => FragmentMerge.Resolve(_splitter, tree, kv.Key, kv.Value, conflicts));
    }

    private FolderModLayer MakeLayer(string name, DepLoadParent parent)
    {
        string dir = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(dir);
        var layer = new FolderModLayer(dir, name);
        string staged = $@"{Container}\{DepLoadContainerSplitter.IdOf(parent.Hash, name)}";
        layer.Stage(NameHash.Compute(staged), staged, "xml",
            Encoding.UTF8.GetBytes(DepLoadXml.FragmentToXml(parent)));
        return layer;
    }

    [Fact]
    public void Canonicalizing_normalises_formatting_before_a_merge_sees_it()
    {
        string ugly = "<Resource crc_ID='16'><CAnimationResource crc_ID='161'></CAnimationResource></Resource>";
        string tidy = DepLoadXml.FragmentToXml(new DepLoadParent(0x10, 0, [new DepLoadChild(0xA1, Animation)]));

        Assert.Equal(tidy, _splitter.Canonicalize(ugly));
    }

    [Fact]
    public void An_unreadable_fragment_names_the_problem()
    {
        byte[] container = DepLoadDocument.Encode(new DepLoadFile([new DepLoadParent(0x10, 0, [])]));

        Assert.ThrowsAny<Exception>(() => _splitter.Apply(
            container, new Dictionary<string, string> { ["16.xml"] = "not xml at all" }));
        Assert.NotNull(_splitter.Open(container).Extract("16.xml"));
        Assert.Null(_splitter.Open(container).Extract("16"));
    }
}
