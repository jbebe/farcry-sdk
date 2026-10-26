using JackAll.Tools.Xbm;

namespace JackAll.Tests;

/// <summary>
/// The shader schema read from the game's descriptors offers what retail materials store, and the
/// lint over it finds what the engine would ignore.
/// </summary>
public sealed class ShaderSchemaTests
{
    [Fact]
    public void A_shader_offers_what_retail_stores()
    {
        if (Schema() is not { } schema)
        {
            return;
        }

        ShaderDescriptor weapon = schema.Find("Weapon")!;
        ShaderParameter colour = weapon.Find("DiffuseColor1")!;
        Assert.Equal((XbmSection.Float, 4, "Diffuse"), (colour.Section, colour.Width, colour.Group));
        Assert.True(colour.IsColor);
        Assert.Equal(XbmSection.Texture, weapon.Find("ReflectionTexture")!.Section);

        // Carried by retail outside the descriptor; composed by the engine and never stored.
        Assert.NotNull(weapon.Find("LogicalMaterialId"));
        Assert.Null(schema.Find("Generic")!.Find("DiffuseTilingAndGroup1"));
        Assert.Null(schema.Find("Generic")!.Find("DiffuseTilings"));

        Assert.Same(schema.Find("Leaf"), schema.Find("RealTreeLeafPure"));
        Assert.DoesNotContain("RealTreeLeafPure", schema.Names);
    }

    [Fact]
    public void Only_a_condition_driven_value_has_a_clean_twin()
    {
        if (Schema() is not { } schema)
        {
            return;
        }

        Assert.Equal("DiffuseColor1Clean", schema.Find("Weapon")!.CleanTwin("DiffuseColor1"));
        Assert.Null(schema.Find("Weapon")!.CleanTwin("DiffuseColor1Clean"));
        Assert.Null(schema.Find("Generic")!.CleanTwin("DiffuseColor1"));
    }

    [Theory]
    [InlineData(XbmFixtures.Wood)]
    [InlineData(XbmFixtures.Metal)]
    public void A_shipped_material_lints_clean(string fixture)
    {
        if (Schema() is not { } schema || Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        Assert.Empty(MaterialLint.Check(XbmFile.Parse(bytes), schema));
    }

    [Fact]
    public void The_lint_finds_what_the_engine_ignores()
    {
        if (Schema() is not { } schema || Fixture.Read(XbmFixtures.Wood) is not { } bytes)
        {
            return;
        }

        XbmFile material = XbmFile.Parse(bytes);
        material.Set(XbmEntry.Parse("specularPower", XbmSection.Float, 1, "8"));
        material.Set(XbmEntry.Parse("DiffuseColor1", XbmSection.Float, 3, "0.2"));
        material.Set(XbmEntry.Parse("MaskTexture1", XbmSection.Texture, 0, @"graphics\missing.xbt"));

        List<MaterialProblem> problems = MaterialLint.Check(material, schema, path => !path.Contains("missing"));

        Assert.Contains(problems, p => p.Key == "specularPower" && p.Message.Contains("'SpecularPower'"));
        Assert.Contains(problems, p => p.Key == "DiffuseColor1" && p.Message.Contains("DiffuseColor1Clean"));
        Assert.Contains(problems, p => p.Key == "MaskTexture1" && p.Message.Contains("missing.xbt"));

        material.Shader = "weapon";
        Assert.Contains("'Weapon'", Assert.Single(MaterialLint.Check(material, schema)).Message);
    }

    private static ShaderSchema? Schema()
        => Fixture.Read(XbmFixtures.Descriptors) is { } xml ? ShaderSchema.Parse(xml) : null;
}
