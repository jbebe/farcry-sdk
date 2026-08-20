using System.Numerics;
using System.Xml.Linq;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// The authored atmosphere out of a world descriptor's Environment block - the values the viewport's
/// fog runs on instead of invented ones.
/// </summary>
public class WorldEnvironmentTests
{
    private const string Fixture = @".\Fixtures\WorldDescriptor\mp_17_dunes.game.xml";

    /// <summary>The retail values, read end to end through the descriptor loader.</summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_retail_descriptor_yields_its_authored_fog()
    {
        if (!File.Exists(Fixture)) return;

        byte[] bytes = File.ReadAllBytes(Fixture);
        WorldEnvironment environment = WorldEnvironment.Load(
            "mp_17_dunes", path => path.EndsWith("mp_17_dunes.game.xml") ? bytes : null);

        Assert.Equal(new Vector3(202f / 255f, 219f / 255f, 230f / 255f), environment.FogColour);
        Assert.Equal(0f, environment.FogStart);
        Assert.Equal(400f, environment.FogEnd);
        Assert.Equal(0.8f, environment.FogAmount);
        Assert.Equal(1024f, environment.ViewDistance);
    }

    /// <summary>The cooker leaves literal f suffixes on some numbers (CurvedHorizon writes
    /// Start="500.0f"), so the parser has to shrug one off rather than fail the field.</summary>
    [Fact]
    public void A_number_with_the_cookers_f_suffix_still_parses()
    {
        WorldEnvironment environment = WorldEnvironment.Read(XElement.Parse(
            """
            <WorldDescriptor>
              <Environment>
                <Fog Color="100,150,200" Start="50.0f" End="800.0F" FogAmount="0.5" />
              </Environment>
            </WorldDescriptor>
            """));

        Assert.Equal(50f, environment.FogStart);
        Assert.Equal(800f, environment.FogEnd);
        Assert.Equal(0.5f, environment.FogAmount);
        Assert.Equal(new Vector3(100f / 255f, 150f / 255f, 200f / 255f), environment.FogColour);
    }

    /// <summary>A world with no descriptor gets the values every retail world ships, not zeros -
    /// zeros would silently switch the fog off.</summary>
    [Fact]
    public void A_missing_descriptor_falls_back_to_the_retail_defaults()
    {
        WorldEnvironment environment = WorldEnvironment.Load("nowhere", _ => null);

        Assert.Equal(WorldEnvironment.Default, environment);
        Assert.True(environment.FogAmount > 0f);
    }

    /// <summary>A partial block degrades field by field: what it names it keeps, what it omits
    /// falls back alone.</summary>
    [Fact]
    public void A_partial_block_keeps_what_it_names()
    {
        WorldEnvironment environment = WorldEnvironment.Read(XElement.Parse(
            """
            <WorldDescriptor>
              <Environment>
                <Fog End="700" />
              </Environment>
            </WorldDescriptor>
            """));

        Assert.Equal(700f, environment.FogEnd);
        Assert.Equal(WorldEnvironment.Default.FogColour, environment.FogColour);
        Assert.Equal(WorldEnvironment.Default.FogAmount, environment.FogAmount);
        Assert.Equal(WorldEnvironment.Default.ViewDistance, environment.ViewDistance);
    }

    [Fact]
    public void A_broken_colour_falls_back_rather_than_guessing()
    {
        WorldEnvironment environment = WorldEnvironment.Read(XElement.Parse(
            """<WorldDescriptor><Environment><Fog Color="oops,1" /></Environment></WorldDescriptor>"""));

        Assert.Equal(WorldEnvironment.Default.FogColour, environment.FogColour);
    }
}
