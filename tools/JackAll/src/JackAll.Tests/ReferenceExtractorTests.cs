using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;
using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Xrefs;

namespace JackAll.Tests;

/// <summary>
/// Each extractor against a real shipped file, asserting concrete edges rather than just counts.
/// </summary>
/// <remarks>
/// The indexer deliberately swallows a decode failure - one malformed entry must not fail a
/// 180,000-file build - which means a broken extractor produces *silence*, not an error, and would
/// otherwise go unnoticed until someone wondered why a panel was empty. These tests are the thing
/// that turns that silence back into a failure.
/// </remarks>
public sealed class ReferenceExtractorTests
{
    /// <summary>A stand-in for a real VFS entry: only the path, hash and type matter to an
    /// extractor, and building a whole <see cref="GameVfs"/> per test would just be slower.</summary>
    private static VfsFile FileFor(string path) => new(
        Hash: JackAll.Core.Format.NameHash.Compute(path),
        Path: path,
        Type: new FileType("test", System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant()),
        Size: 0,
        SourceName: "test",
        SourceKind: SourceKind.Archive,
        IsOverriding: false,
        NameIsKnown: true);

    private static ReferenceSink Extract(IReferenceExtractor extractor, string path, byte[] content)
    {
        var sink = new ReferenceSink(FcbClassDefinitions.Empty);
        VfsFile file = FileFor(path);
        Assert.True(extractor.CanHandle(file), $"{extractor.GetType().Name} should claim {path}");
        sink.BeginFile((uint)file.Hash);
        extractor.Extract(file, content, sink);
        return sink;
    }

    [Fact]
    public void Depload_extractor_reports_every_parent_child_pair()
    {
        string path = Path.Combine("Fixtures", "DepLoad", "entitylibrary_depload.dat");
        if (!File.Exists(path)) return; // fixture not present in this checkout

        ReferenceSink sink = Extract(new DepLoadReferenceExtractor(), "worlds\\x\\entitylibrary_depload.dat",
            File.ReadAllBytes(path));

        // 433 parents / 1314 children, the counts DepLoadDocument's own remarks record for this file.
        // Every child yields two edges: the dependency itself and its type tag.
        Assert.Equal(1314, sink.Edges.Count(e => e.Kind == RefKind.DepLoadDependency));
        Assert.All(sink.Edges.Where(e => e.Kind == RefKind.DepLoadDependency),
            e => Assert.Equal(RefSpace.FilePath, e.TargetSpace));

        // Exactly 8 distinct type hashes across all children - the deduplicated type table's size.
        Assert.Equal(8, sink.Edges
            .Where(e => e.Kind == RefKind.DepLoadTypeTag)
            .Select(e => e.Target)
            .Distinct()
            .Count());
    }

    [Fact]
    public void Xbm_extractor_reports_texture_slots_by_name()
    {
        string? path = FirstFixture(Path.Combine("Fixtures", "Xbm"), "*.xbm");
        if (path is null) return; // fixture not present in this checkout

        ReferenceSink sink = Extract(new XbmReferenceExtractor(), "graphics\\_materials\\test.xbm",
            File.ReadAllBytes(path!));

        Assert.NotEmpty(sink.Edges);
        Assert.All(sink.Edges, e =>
        {
            Assert.Equal(RefKind.XbmTexture, e.Kind);
            Assert.Equal(RefSpace.FilePath, e.TargetSpace);
        });

        // The slot name is the whole point of the site: "which slot of which material" is what makes
        // an xref row actionable rather than merely true.
        Assert.Contains(sink.Names.Values, name => name.Contains("Texture", StringComparison.Ordinal));
    }

    [Fact]
    public void Spk_extractor_defines_every_record_id()
    {
        string? path = FirstFixture(Path.Combine("Fixtures", "Spk"), "*.spk");
        if (path is null) return; // fixture not present in this checkout

        ReferenceSink sink = Extract(new SpkReferenceExtractor(), "soundbinary\\0000abcd.spk",
            File.ReadAllBytes(path!));

        Assert.NotEmpty(sink.Definitions);
        Assert.All(sink.Definitions, d => Assert.Equal(RefSpace.SoundResource, d.Space));
    }

    [Fact]
    public void Mgb_extractor_reports_name_ids_and_texture_paths()
    {
        string? path = FirstFixture(Path.Combine(TestSupport.RepositoryRoot, "tmp", "menu"), "*.mgb");
        if (path is null) return; // fixture not present in this checkout

        ReferenceSink sink = Extract(new MgbReferenceExtractor(), "ui\\test.mgb", File.ReadAllBytes(path!));

        // A package with no NameId at all would mean the visitor codec never reached the body.
        Assert.Contains(sink.Edges, e => e.Kind == RefKind.MgbNameId);
        Assert.All(sink.Edges.Where(e => e.Kind == RefKind.MgbNameId),
            e => Assert.Equal(RefSpace.EngineName, e.TargetSpace));
    }

    [Fact]
    public void Fcb_extractor_reports_string_paths_and_hash_values()
    {
        string? path = FirstFixture(Path.Combine("Fixtures", "Fcb"), "*.fcb");
        if (path is null) return; // fixture not present in this checkout

        var sink = new ReferenceSink(BundledClasses.Value);
        VfsFile file = FileFor("worlds\\x\\generated\\entitylibrary.fcb");
        sink.BeginFile((uint)file.Hash);
        new FcbReferenceExtractor().Extract(file, File.ReadAllBytes(path!), sink);

        // Without the class definitions every value is opaque, so this doubles as a check that the
        // bundled binary_classes.xml is actually being found by the test host.
        Assert.NotEmpty(sink.Edges);
    }

    [Fact]
    public void Text_extractor_only_accepts_paths_with_a_real_game_extension()
    {
        const string content = """
            <package>
              <file path="graphics\ui\hud.xbt" />
              <signal name="OnPlayerDied" />
              <version value="1.0.3" />
              <bad path="Some\Signal\Name" />
            </package>
            """;

        ReferenceSink sink = Extract(new TextReferenceExtractor(), "ui\\test.mgb.desc",
            System.Text.Encoding.UTF8.GetBytes(content));

        Assert.Single(sink.Edges);
        Assert.Equal(JackAll.Core.Format.NameHash.Compute(@"graphics\ui\hud.xbt"), sink.Edges[0].Target);
    }

    private static string? FirstFixture(string directory, string pattern)
        => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, pattern).Order().FirstOrDefault()
            : null;

    private static readonly Lazy<FcbClassDefinitions> BundledClasses = new(JackAll.Core.BundledAssets.LoadFcbClasses);
}
