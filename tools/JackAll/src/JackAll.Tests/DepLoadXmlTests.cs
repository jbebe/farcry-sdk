using JackAll.Core;
using JackAll.Core.Format;
using JackAll.Core.Naming;

namespace JackAll.Tests;

/// <summary>
/// The XML layer's gate is the same one the binary codec has: a shipped file has to survive the trip
/// out to text and back. Names are resolved on the way out, so the run also covers the path-labelled
/// form a mod author actually edits, not just bare hashes.
/// </summary>
public class DepLoadXmlTests
{
    private static readonly NameDatabase Names = BundledAssets.LoadNames();

    public static TheoryData<string> CorpusFiles() => DepLoadDocumentTests.CorpusFiles();

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Round_trips_every_shipped_depload_through_xml(string path)
    {
        if (path.Length == 0) return;

        byte[] original = File.ReadAllBytes(path);
        byte[] rebuilt = DepLoadXml.Encode(DepLoadXml.Decode(original, Names));

        Assert.Equal(original.Length, rebuilt.Length);
        int at = Fc2Corpus.FirstDifference(original, rebuilt);
        Assert.True(at < 0, Fc2Corpus.DescribeDifference(path, original, rebuilt));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_hashlist_is_present_so_the_named_form_is_actually_covered()
    {
        Assert.True(Names.Count > 0,
            "No hashlist beside the test binary, so every ID attribute was skipped and the "
            + "path-labelled form went untested. Build the CLI once to copy assets/fc2.hashlist.");
    }

    [Fact]
    public void A_child_is_tagged_with_its_resource_class()
    {
        var file = new DepLoadFile([
            new DepLoadParent(0x20, 0, [new DepLoadChild(0xAA, DepLoadTypes.Hash("CAnimationResource"))]),
        ]);

        string xml = DepLoadXml.ToXml(file);
        DepLoadFile back = DepLoadXml.FromXml(xml);

        Assert.Contains("<CAnimationResource", xml, StringComparison.Ordinal);
        Assert.Equal(0x20u, back.Parents[0].Hash);
        Assert.Equal(file.Parents[0].Children, back.Parents[0].Children);
    }

    [Fact]
    public void A_new_entry_can_be_written_as_a_path_with_no_hash()
    {
        string path = @"graphics\characters\_common\animations\weapons\special\x.mab";
        string xml = $"""
            <depload>
              <Resource ID="dart_rifle">
                <CAnimationResource ID="{path}" />
              </Resource>
            </depload>
            """;

        DepLoadFile file = DepLoadXml.FromXml(xml);

        Assert.Equal(NameHash.Compute("dart_rifle"), file.Parents[0].Hash);
        Assert.Equal(NameHash.Compute(path), file.Parents[0].Children[0].Hash);
        Assert.Equal(DepLoadTypes.Hash("CAnimationResource"), file.Parents[0].Children[0].TypeHash);
    }

    [Fact]
    public void A_path_that_disagrees_with_its_hash_is_rejected()
    {
        string xml = """
            <depload>
              <Resource ID="dart_rifle" crc_ID="1" />
            </depload>
            """;

        Assert.Throws<InvalidDataException>(() => DepLoadXml.FromXml(xml));
    }

    /// <summary>
    /// The animation package the VSS work needs. Its name hashes as a path does, which is what lets a
    /// caller name a package rather than look its CRC up.
    /// </summary>
    [Fact]
    public void An_animation_package_name_hashes_like_a_path()
    {
        Assert.Equal(115510436u, NameHash.Compute("dart_rifle"));
    }

    [Fact]
    public void Every_known_class_name_hashes_to_its_own_type_id()
    {
        Assert.Equal("CAnimationResource", DepLoadTypes.NameOf(0xB0604725));
        Assert.Equal("CAnimationPackageResource", DepLoadTypes.NameOf(0x84A30AF0));
        Assert.Equal("CTextureResource", DepLoadTypes.NameOf(0x6BD55AFC));
        Assert.Null(DepLoadTypes.NameOf(0xDEADBEEF));
    }
}
