using JackAll.Tools.Labels;
using JackAll.Tools.Shader;
using JackAll.Tools.ShaderLabels;

namespace JackAll.Tests;

/// <summary>
/// The shipped fastinitdata, render config and pixel index name and label real objects; a small
/// synthetic render config pins the quality rules one at a time.
/// </summary>
public class ShaderLabelTests
{
    private const string InitData = "Shader/fastinitdata_d3d9.bin";
    private const string RenderConfig = "Shader/defaultrenderconfig.xml";
    private const string Index = "Shader/index.pso";

    private const string Config = """
        <Profile>
          <RenderQuality>
            <quality id="low" VegetationQuality="low" ShadowQuality="off" />
            <quality id="high" VegetationQuality="high" ShadowQuality="high" />
          </RenderQuality>
          <Vegetation>
            <quality id="low"><Shaders>
              <shader name="Mesh_Grass" add="GRASSQUALITYLOW" remove="" />
              <shader name="AAA" remove="NORMALMAP,REFLECTION" />
            </Shaders></quality>
            <quality id="high"><Shaders><shader name="Mesh_Grass" add="GRASSQUALITYHIGH" /></Shaders></quality>
            <quality id="console"><Shaders><shader name="Mesh_Grass" add="GRASSQUALITYCONSOLE" /></Shaders></quality>
          </Vegetation>
          <Shadow>
            <quality id="off" DisableShadow="1" />
            <quality id="high" DisableShadow="0" />
          </Shadow>
          <Post><quality id="low" /></Post>
        </Profile>
        """;

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(InitData, RenderConfig, Index);

    [Fact]
    public void The_shipped_table_lists_every_shader_and_permutation()
    {
        if (Fixture.Read(InitData) is not { } bytes) return;

        ShaderInitData init = ShaderInitData.Parse(bytes);

        Assert.Equal(86, init.Shaders.Count);
        Assert.Equal(147_828, init.Permutations.Count);
    }

    [Theory]
    [InlineData("celestialbody", "TEXKILL ADDITIVE TIME_OF_DAY_COLOR FAKEHDR", 1u, "celestialbody_3_4_2_5")]
    [InlineData("burnterrain", "NBR_FIRE_NODES", 32u, "burnterrain_0-31of31")]
    [InlineData("burnterrain", "", 1u, "burnterrain")]
    public void A_permutation_is_named_by_its_options_define_indices(string shaderName, string options, uint value, string expected)
    {
        if (Fixture.Read(InitData) is not { } bytes) return;

        ShaderDefinition shader = ShaderInitData.Parse(bytes).Shaders.Single(s => s.Name == shaderName);

        Assert.Equal(expected, shader.PermutationName(Mask(shader, options, value)));
    }

    [Fact]
    public void Every_key_of_the_pixel_index_is_a_named_permutation()
    {
        if (Fixture.Read(InitData) is not { } bytes || Fixture.Read(Index) is not { } index) return;

        ShaderInitData init = ShaderInitData.Parse(bytes);
        HashSet<uint> named = [.. init.Permutations.Select(m => ShaderIndex.KeyOf(init.ShaderOf(m).PermutationName(m)))];

        Assert.All(ShaderIndex.Parse(index).Keys, key => Assert.Contains(key, named));
    }

    [Theory]
    [InlineData(@"h0f\shadernumber_fc7bd88f.pso", "weapon",
        "mesh_weapon · lit by spot · QUALITYLOW, receives shadows · no preset", "Texture = low; Shadow ≥ low; 12 permutations")]
    [InlineData(@"h7f\shadernumber_0eddc97f.pso", "ground",
        "road · lit by omni · DIFFUSE_SIMPLE, NORMALMAP, PARALLAX · ultrahigh", "Terrain = ultrahigh; 1 permutation")]
    [InlineData(@"h53\shadernumber_71845653.pso", "sky",
        "cloudlayer · CLOUD_QUALITY_LOW, LAYER1 · low", "Environment = low; 1 permutation")]
    public void A_shipped_object_is_labelled_by_its_permutations_and_presets(string path, string category, string detail, string evidence)
    {
        if (Fixture.Read(InitData) is not { } bytes || Fixture.ReadText(RenderConfig) is not { } xml
            || Fixture.Read(Index) is not { } index) return;

        FileLabel label = ShaderLabeler.Label(ShaderInitData.Parse(bytes), RenderQualityConfig.Parse(xml),
            new Dictionary<string, ShaderIndex> { ["pso"] = ShaderIndex.Parse(index) })
            .Single(l => l.Path == @"engine\shaders\obj\" + path);

        Assert.Equal((category, detail, ShaderLabeler.FromIndex), (label.Category, label.Detail, label.Source));
        Assert.StartsWith(evidence, label.Evidence);
    }

    [Fact]
    public void Only_settings_that_change_shader_options_are_kept_with_their_pc_levels()
    {
        RenderQualityConfig config = RenderQualityConfig.Parse(Config);

        Assert.Equal(["Vegetation", "Shadow"], config.Settings.Select(s => s.Name));
        Assert.Equal(["low", "high"], config.Settings[0].Levels.Select(l => l.Id));
        Assert.Equal(1, config.Settings[0].PresetLevels["high"]);
    }

    [Theory]
    [InlineData("Mesh_Grass", "GRASSQUALITYLOW INSTANCING", true, false)]
    [InlineData("mesh_grass", "GRASSQUALITYHIGH", false, true)]
    [InlineData("mesh_grass", "INSTANCING", true, true)]
    [InlineData("aaa", "NORMALMAP", false, true)]
    [InlineData("aaa", "DIFFUSE_MAP_BASE", true, true)]
    public void A_level_drops_what_it_removes_and_the_adds_of_other_levels(string shader, string options, bool low, bool high)
    {
        QualitySetting vegetation = RenderQualityConfig.Parse(Config).Settings[0];
        HashSet<string> set = [.. options.Split(' ')];

        Assert.Equal((low, high), (vegetation.Allows(vegetation.Levels[0], shader, set), vegetation.Allows(vegetation.Levels[1], shader, set)));
    }

    [Theory]
    [InlineData("SHADOW", false)]
    [InlineData("RECEIVE_SHADOWS", false)]
    [InlineData("SUN", true)]
    public void Shadows_off_drops_shadow_maps_and_receivers(string option, bool drawn)
    {
        QualitySetting shadow = RenderQualityConfig.Parse(Config).Settings[1];

        Assert.Equal(drawn, shadow.Allows(shadow.Levels[0], "aaa", new HashSet<string> { option }));
    }

    private static ulong Mask(ShaderDefinition shader, string options, uint value)
        => options.Split(' ', StringSplitOptions.RemoveEmptyEntries).Aggregate((ulong)shader.Number, (mask, name) =>
        {
            ShaderOption option = shader.Options.Single(o => o.Name == name);
            return mask | ((ulong)(option.Max is null ? 1 : value) << option.Bit);
        });
}
