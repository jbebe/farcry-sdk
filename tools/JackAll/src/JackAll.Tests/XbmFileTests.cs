using JackAll.Tools.Xbg;
using JackAll.Tools.Xbm;

namespace JackAll.Tests;

/// <summary>
/// A shipped `.xbm`'s LTMD body decodes and re-serialises to its bytes, and a mesh that embeds its
/// material instead parses the other way.
/// </summary>
/// <remarks>
/// <see cref="XbgFileTests"/> already round-trips the container while carrying LTMD opaque, so this
/// is the half that proves the body itself is understood.
/// </remarks>
public sealed class XbmFileTests
{
    [Theory]
    [InlineData(XbmFixtures.Wood)]
    [InlineData(XbmFixtures.Blended)]
    public void Reserialises_a_shipped_material_byte_for_byte(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        Fixture.AssertSameBytes(fixture, original, XbmFile.Parse(original).Write());
    }

    /// <summary>
    /// An entry list, not a map: one shipped material repeats a key inside a section, and a reader
    /// that only kept a map could not put it back.
    /// </summary>
    [Fact]
    public void A_repeated_key_survives_the_round_trip()
    {
        if (Fixture.Read(XbmFixtures.RepeatedKey) is not { } original)
        {
            return;
        }

        XbmFile material = XbmFile.Parse(original);
        Assert.NotEqual(
            material.Textures.Count + material.Floats.Count + material.Integers.Count,
            material.Entries.Count);
        Fixture.AssertSameBytes(XbmFixtures.RepeatedKey, original, material.Write());
    }

    /// <summary>
    /// A mesh that defines its material inline rather than naming an `.xbm`, whose LTMD leads with
    /// the name and part instead of the five-byte preamble.
    /// </summary>
    [Fact]
    public void Inline_materials_parse_with_their_own_layout()
    {
        if (Fixture.Read(XbgFixtures.Bat) is not { } bytes)
        {
            return;
        }

        Dictionary<string, XbmFile> inline = XbmFile.InlineMaterials(XbgFile.Parse(bytes));
        Assert.NotEmpty(inline);
        foreach ((string name, XbmFile material) in inline)
        {
            Assert.NotEmpty(name);
            Assert.NotEmpty(material.Shader);
            // The part it applies to is what an embedded material carries and a standalone one
            // does not.
            Assert.NotEmpty(material.Part);
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(
            XbmFixtures.Wood, XbmFixtures.Metal, XbmFixtures.Blended, XbmFixtures.Masked, XbmFixtures.RepeatedKey);
}
