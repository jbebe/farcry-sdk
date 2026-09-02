using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;

namespace JackAll.Tests;

/// <summary>
/// A `depload.dat` is entirely references, so the xref index is how the app answers both directions
/// for it: what a resource pulls in, and what pulls a resource in. These pin the edge shape the
/// Xrefs panel relies on - in particular that every edge is *sited* by the resource that declares
/// it, which is what lets one fragment row report its own dependencies instead of the whole file's.
/// </summary>
public class DepLoadReferenceExtractorTests
{
    public static TheoryData<string> CorpusFiles() => DepLoadDocumentTests.CorpusFiles();

    /// <summary>The campaign world, or null on a checkout with no game export to read.</summary>
    private static string? World1()
        => Fc2Corpus.Find("_depload.dat")
            .FirstOrDefault(p => Path.GetFileName(p).Equals("world1_depload.dat", StringComparison.OrdinalIgnoreCase));

    private static (IReadOnlyList<RefEdge> Edges, DepLoadFile File) Extract(string path)
    {
        byte[] content = File.ReadAllBytes(path);
        var sink = new ReferenceSink(FcbClassDefinitions.Empty);
        var file = new VfsFile(
            Hash: NameHash.Compute(path), Path: @"worlds\w\generated\w_depload.dat",
            Type: new FileType("misc", "dat"), Size: content.Length, SourceName: "test",
            SourceKind: SourceKind.Archive, IsOverriding: false, NameIsKnown: true);

        sink.BeginFile(file.EngineHash);
        new DepLoadReferenceExtractor().Extract(file, content, sink);
        return (sink.Edges, DepLoadDocument.Decode(content));
    }

    [Fact]
    public void The_extractor_claims_a_depload_and_nothing_else()
    {
        var extractor = new DepLoadReferenceExtractor();
        Assert.True(extractor.CanHandle(Row("world1_depload.dat", "dat")));
        Assert.False(extractor.CanHandle(Row("patch.dat", "dat")));
        Assert.False(extractor.CanHandle(Row("entitylibrary.fcb", "fcb")));

        static VfsFile Row(string name, string ext) => new(
            Hash: 1, Path: @"worlds\w\generated\" + name, Type: new FileType("misc", ext), Size: 0,
            SourceName: "t", SourceKind: SourceKind.Archive, IsOverriding: false, NameIsKnown: true);
    }

    /// <summary>
    /// One edge per dependency, each sited by the resource that declares it. The Xrefs panel filters
    /// on that site to answer for a single fragment row, so a drifting site key would silently turn
    /// "what this resource needs" into "everything in this world".
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Every_dependency_becomes_an_edge_sited_by_the_resource_that_declares_it(string path)
    {
        if (path.Length == 0) return;

        (IReadOnlyList<RefEdge> edges, DepLoadFile file) = Extract(path);

        RefEdge[] dependencies = [.. edges.Where(e => e.Kind == RefKind.DepLoadDependency)];
        Assert.Equal(file.Parents.Sum(p => p.Children.Count), dependencies.Length);

        foreach (DepLoadParent parent in file.Parents.Take(50))
        {
            uint[] sited = [.. dependencies.Where(e => e.SiteKey == parent.Hash).Select(e => e.Target)];
            Assert.Equal([.. parent.Children.Select(c => c.Hash)], sited);
        }
    }

    /// <summary>
    /// The other direction, which is what "Referenced by" shows on the depended-on file: a clip that
    /// only one animation package lists resolves back to exactly that package.
    /// </summary>
    [Fact]
    public void A_dependency_resolves_back_to_the_resource_that_lists_it()
    {
        if (World1() is not { } world1) return;

        const uint DartRifle = 115510436;    // CAnimationPackageResource "dart_rifle"
        const uint DartReload = 0x70AEAAE4;  // ...\pneu_dart_model_389\1stge_uppb_reload_+000fw_sp389_i1.mab

        (IReadOnlyList<RefEdge> edges, _) = Extract(world1);

        uint[] listedBy = [.. edges
            .Where(e => e.Kind == RefKind.DepLoadDependency && e.Target == DartReload)
            .Select(e => e.SiteKey)
            .Distinct()];

        // Exactly one, and it is the weapon's animation package - a clip is reachable only through
        // the package that plays it, which is the whole reason registering one matters.
        Assert.Equal([DartRifle], listedBy);
    }

    /// <summary>Each dependency also carries its resource class, which the panel shows as the kind.</summary>
    [Fact]
    public void A_dependencys_resource_class_is_indexed_alongside_it()
    {
        if (World1() is not { } world1) return;

        (IReadOnlyList<RefEdge> edges, _) = Extract(world1);

        Assert.Contains(edges, e => e.Kind == RefKind.DepLoadTypeTag
            && e.TargetSpace == RefSpace.DepLoadType
            && e.Target == DepLoadTypes.Hash("CAnimationResource"));
    }
}
