using System.Xml.Linq;
using JackAll.Core.Format.Rml;

namespace JackAll.Tests;

/// <summary>
/// Runs against real shipped .rml files, the only authority on the format being what Ubisoft
/// actually shipped.
/// </summary>
public class RmlDocumentTests
{
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent("Rml/dlc1_toc.rml", "Rml/dlc_jungle_toc.rml");

    [Theory]
    // Two small DLC manifests.
    [InlineData("Rml/dlc1_toc.rml")]
    [InlineData("Rml/dlc_jungle_toc.rml")]
    // A string table: thousands of nodes and heavy string-table reuse.
    [InlineData(StringTableContainerSplitterTests.English)]
    public void Reserializing_a_shipped_rml_reproduces_it_byte_for_byte(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        XElement root = RmlDocument.Deserialize(original);
        byte[] rewritten = RmlDocument.Serialize(root);

        Assert.Equal(original, rewritten);
    }

    [Fact]
    public void A_0xFE_marker_reads_a_u32_like_0xFF_does()
    {
        var root = new XElement("Root", new XAttribute("text", new string('x', 300)));
        byte[] rml = RmlDocument.Serialize(root);
        // The string table outgrows a one-byte count, so its size is the first marked value.
        Assert.Equal(0xFF, rml[2]);
        rml[2] = 0xFE;

        Assert.Equal(root.ToString(), RmlDocument.Deserialize(rml).ToString());
    }

    [Fact]
    public void Decoding_the_jungle_dlc_manifest_produces_the_expected_content()
    {
        if (Fixture.Read("Rml/dlc_jungle_toc.rml") is not { } original) return;

        XElement root = RmlDocument.Deserialize(original);

        Assert.Equal("DLC", root.Name.LocalName);
        Assert.Equal("Jungle", (string?)root.Attribute("name"));
        XElement? map = root.Element("MapService")?.Element("Maps")?.Element("Map");
        Assert.Equal("Jungle Seizure", (string?)map?.Attribute("displayName"));
        Assert.Equal("2001", (string?)map?.Attribute("id"));
    }
}
