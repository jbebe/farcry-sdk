using JackAll.Tools.Xbm;

namespace JackAll.Tests;

/// <summary>
/// Run against real .xbm files rather than a synthetic fixture, for the same reason as
/// <see cref="XbtTextureTests"/>: the only authority on what the engine actually writes is what it
/// actually shipped.
/// </summary>
public class XbmMaterialTests
{
    [Theory]
    [InlineData(XbmFixtures.Wood)]
    [InlineData(XbmFixtures.Metal)]
    public void A_shipped_xbm_parses_with_a_non_empty_name_and_template(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes) return;

        XbmMaterial material = XbmMaterial.Parse(bytes);

        Assert.False(string.IsNullOrWhiteSpace(material.Name));
        Assert.False(string.IsNullOrWhiteSpace(material.Template));
    }

    [Theory]
    [InlineData(XbmFixtures.Wood)]
    [InlineData(XbmFixtures.Metal)]
    public void Every_texture_slot_points_at_an_xbt(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes) return;

        XbmMaterial material = XbmMaterial.Parse(bytes);

        Assert.NotEmpty(material.Textures);
        Assert.All(material.Textures, tex => Assert.EndsWith(".xbt", tex.Value, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_rejects_a_file_without_the_HSEM_header()
        => Assert.Throws<InvalidDataException>(() => XbmMaterial.Parse("not an xbm file at all"u8.ToArray()));

    [Fact]
    public void Parse_rejects_an_HSEM_file_with_no_LTMD_chunk()
        => Assert.Throws<InvalidDataException>(() => XbmMaterial.Parse("HSEMno material chunk here"u8.ToArray()));
}
