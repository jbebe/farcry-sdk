using JackAll.Core.Xrefs;

namespace JackAll.Tests;

/// <summary>
/// The index's own layout and query contract, independent of any extractor.
/// </summary>
/// <remarks>
/// Every query here is a binary search over a sorted, byte-backed span, which is the kind of code
/// that works perfectly on a middle element and silently returns nothing at the ends. The boundary
/// cases below are the point of this file; the round-trip test exists because an index built in
/// memory and one read back from disk go through the same bytes and must therefore be
/// indistinguishable.
/// </remarks>
public sealed class ReferenceIndexTests
{
    private const uint FileA = 0x1111_1111;
    private const uint FileB = 0x2222_2222;
    private const uint FileC = 0xFFFF_FF00;

    private static ReferenceIndex Sample() => ReferenceIndex.Build(
        [
            new RefEdge(FileA, RefSpace.FilePath, 0x0000_0001, RefKind.FcbPathValue, 0xAAAA, 0),
            new RefEdge(FileA, RefSpace.FilePath, 0x0000_0002, RefKind.FcbPathValue, 0xAAAA, 1),
            new RefEdge(FileB, RefSpace.FilePath, 0x0000_0001, RefKind.XbmTexture, 0xBBBB, 0),
            new RefEdge(FileB, RefSpace.EngineName, 0x0000_0001, RefKind.FcbNameValue, 0xBBBB, 0),
            new RefEdge(FileC, RefSpace.SoundResource, 0xFFFF_FFFE, RefKind.SpkRecordLink, 0xCCCC, 0),
        ],
        [
            new RefDefinition(RefSpace.SoundResource, 0xFFFF_FFFE, FileC, 0xCCCC),
        ],
        new Dictionary<uint, string> { [0xAAAA] = "fileMuzzleFx", [0xBBBB] = "DiffuseTexture1" },
        [FileA, FileB, FileC, 0x3333_3333]);

    [Fact]
    public void References_to_a_target_span_every_source_but_only_that_space()
    {
        ReferenceIndex index = Sample();

        // Target 1 exists in two spaces at once - exactly the collision the typed spaces exist to
        // keep apart, since a path hash and a name hash are the same function over different input.
        IReadOnlyList<RefEdge> paths = index.ReferencesTo(RefSpace.FilePath, 1);
        Assert.Equal(2, paths.Count);
        Assert.All(paths, e => Assert.Equal(RefSpace.FilePath, e.TargetSpace));

        IReadOnlyList<RefEdge> names = index.ReferencesTo(RefSpace.EngineName, 1);
        Assert.Single(names);
        Assert.Equal(FileB, names[0].SourceFile);
    }

    [Fact]
    public void References_from_a_file_returns_only_that_file()
    {
        ReferenceIndex index = Sample();

        Assert.Equal(2, index.ReferencesFrom(FileA).Count);
        Assert.Equal(2, index.ReferencesFrom(FileB).Count);
        Assert.All(index.ReferencesFrom(FileA), e => Assert.Equal(FileA, e.SourceFile));
    }

    [Fact]
    public void Boundary_lookups_hit_the_first_and_last_record()
    {
        ReferenceIndex index = Sample();

        // Lowest (space, target) in the whole edge array, and the highest - a lower-bound search that
        // is off by one at either end returns an empty list here and nowhere else.
        Assert.NotEmpty(index.ReferencesTo(RefSpace.FilePath, 1));
        Assert.NotEmpty(index.ReferencesTo(RefSpace.SoundResource, 0xFFFF_FFFE));

        // Same for the by-source permutation.
        Assert.NotEmpty(index.ReferencesFrom(FileA));
        Assert.NotEmpty(index.ReferencesFrom(FileC));
    }

    [Fact]
    public void Missing_lookups_return_empty_rather_than_the_neighbouring_record()
    {
        ReferenceIndex index = Sample();

        Assert.Empty(index.ReferencesTo(RefSpace.FilePath, 3));
        Assert.Empty(index.ReferencesTo(RefSpace.OasisString, 1));
        Assert.Empty(index.ReferencesFrom(0x9999_9999));
        Assert.False(index.TryGetDefinition(RefSpace.SoundResource, 0x1234, out _));
    }

    [Fact]
    public void An_indexed_file_with_no_references_is_still_recorded_as_indexed()
    {
        ReferenceIndex index = Sample();

        // The distinction the whole incremental rebuild rests on: "visited, found nothing" must not
        // be confusable with "never visited", or every launch would re-extract those files forever.
        Assert.True(index.IsIndexed(0x3333_3333));
        Assert.Empty(index.ReferencesFrom(0x3333_3333));
        Assert.False(index.IsIndexed(0x4444_4444));
    }

    [Fact]
    public void Saving_and_loading_reproduces_every_answer()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jackall-xref-{Guid.NewGuid():N}.bin");
        try
        {
            ReferenceIndex original = Sample();
            original.Save(path);
            ReferenceIndex reloaded = ReferenceIndex.Load(path);

            Assert.Equal(original.EdgeCount, reloaded.EdgeCount);
            Assert.Equal(original.DefinitionCount, reloaded.DefinitionCount);
            Assert.Equal(original.IndexedFileCount, reloaded.IndexedFileCount);
            Assert.Equal(original.ReferencesTo(RefSpace.FilePath, 1), reloaded.ReferencesTo(RefSpace.FilePath, 1));
            Assert.Equal(original.ReferencesFrom(FileB), reloaded.ReferencesFrom(FileB));
            Assert.Equal("fileMuzzleFx", reloaded.Name(0xAAAA));
            Assert.Equal(original.AllNames(), reloaded.AllNames());

            Assert.True(reloaded.TryGetDefinition(RefSpace.SoundResource, 0xFFFF_FFFE, out RefDefinition definition));
            Assert.Equal(FileC, definition.DefiningFile);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_corrupt_or_missing_file_loads_as_an_empty_index()
    {
        // Every byte of this file is re-derivable, so an unreadable one must degrade to "nothing
        // indexed yet" rather than taking the app down on startup.
        string path = Path.Combine(Path.GetTempPath(), $"jackall-xref-{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, [0xDE, 0xAD, 0xBE, 0xEF, 0x01]);
            Assert.Equal(0, ReferenceIndex.Load(path).EdgeCount);
            Assert.Equal(0, ReferenceIndex.Load(path + ".nope").EdgeCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_overlay_shadows_a_base_file_rather_than_adding_to_it()
    {
        ReferenceIndex baseIndex = Sample();

        // A mod replaces FileB and drops its texture reference, adding a different one instead.
        var overlay = new ReferenceHarvest(
            [new RefEdge(FileB, RefSpace.FilePath, 0x0000_0009, RefKind.XbmTexture, 0xBBBB, 0)],
            [],
            new Dictionary<uint, string>(),
            [FileB],
            []);
        var graph = new ReferenceGraph(baseIndex, overlay);

        // The dropped reference is genuinely gone: the engine no longer follows it, so neither may
        // the graph. This is the case a naive "base plus overlay" union gets wrong.
        Assert.DoesNotContain(graph.ReferencesTo(RefSpace.FilePath, 1), e => e.SourceFile == FileB);
        Assert.Contains(graph.ReferencesTo(RefSpace.FilePath, 1), e => e.SourceFile == FileA);
        Assert.Single(graph.ReferencesFrom(FileB));
        Assert.Equal(0x0000_0009u, graph.ReferencesFrom(FileB)[0].Target);

        // An untouched file still answers from the base index.
        Assert.Equal(2, graph.ReferencesFrom(FileA).Count);
    }

    [Fact]
    public void Site_text_falls_back_to_hex_and_appends_an_array_index()
    {
        var graph = new ReferenceGraph(Sample(), new ReferenceHarvest([], [], new Dictionary<uint, string>(), [], []));

        Assert.Equal("fileMuzzleFx", graph.DescribeSite(new RefEdge(FileA, RefSpace.FilePath, 1, RefKind.FcbPathValue, 0xAAAA, 0)));
        Assert.Equal("fileMuzzleFx[1]", graph.DescribeSite(new RefEdge(FileA, RefSpace.FilePath, 2, RefKind.FcbPathValue, 0xAAAA, 1)));
        Assert.Equal("#0000DEAD", graph.DescribeSite(new RefEdge(FileA, RefSpace.FilePath, 2, RefKind.FcbPathValue, 0xDEAD, 0)));
    }
}
