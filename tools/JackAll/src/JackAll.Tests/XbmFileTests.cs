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

    /// <summary>A created material reads back as what it was given, in the container shipped ones use.</summary>
    [Fact]
    public void A_created_material_reads_back()
    {
        XbmFile created = XbmFile.Create("NEW_METAL", "Weapon");
        created.Add(XbmEntry.Parse("DiffuseTexture1", XbmSection.Texture, 0, @"graphics\new\albedo.xbt"));
        created.Add(XbmEntry.Parse("DiffuseColor1", XbmSection.Float, 3, "0.5"));
        created.Add(XbmEntry.Parse("LogicalMaterialId", XbmSection.Integer, 0, "4"));

        XbmFile read = XbmFile.Parse(created.Write());

        Assert.Equal("NEW_METAL", read.Name);
        Assert.Equal("Weapon", read.Shader);
        Assert.Equal(@"graphics\new\albedo.xbt", read.Textures["DiffuseTexture1"]);
        Assert.Equal([0.5f, 0.5f, 0.5f], read.Floats["DiffuseColor1"]);
        Assert.Equal(4u, read.Integers["LogicalMaterialId"]);
    }

    /// <summary>
    /// Setting a key changes that key alone, in its own place: setting one to the value it already
    /// holds writes the file back unchanged.
    /// </summary>
    [Fact]
    public void Set_keeps_a_key_in_place()
    {
        if (Fixture.Read(XbmFixtures.Wood) is not { } original)
        {
            return;
        }

        XbmFile material = XbmFile.Parse(original);
        foreach (XbmEntry entry in material.Entries.ToList())
        {
            MaterialEdit.Set(material, null, entry.Key, entry.Text, link: false);
        }
        Fixture.AssertSameBytes(XbmFixtures.Wood, original, material.Write());

        material.Set(XbmEntry.Parse("SpecularPower", XbmSection.Float, 1, "150"));
        Assert.Equal([150f], XbmFile.Parse(material.Write()).Floats["SpecularPower"]);
        Assert.True(material.Remove("SpecularPower"));
        Assert.False(XbmFile.Parse(material.Write()).Floats.ContainsKey("SpecularPower"));
    }

    [Theory]
    [InlineData(3, "1, 2", "takes 3 numbers")]
    [InlineData(1, "x", "'x' is not one")]
    public void A_value_that_does_not_fit_is_refused(int width, string text, string message)
        => Assert.Contains(message,
            Assert.Throws<FormatException>(() => XbmEntry.Parse("Key", XbmSection.Float, width, text)).Message);

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(
            XbmFixtures.Wood, XbmFixtures.Metal, XbmFixtures.Blended, XbmFixtures.Masked, XbmFixtures.RepeatedKey,
            XbmFixtures.Descriptors);
}
