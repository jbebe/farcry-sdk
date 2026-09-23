using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// A shipped `.xbg` or `.xbm` container re-serialises to its own bytes.
/// </summary>
/// <remarks>
/// The writer regenerates every chunk size, payload size, sub-chunk count and the header's own byte
/// count rather than echoing what it parsed, so a pass means the framing is genuinely understood.
/// An `.xbm` is the same container with a material chunk and no geometry, which is why both run
/// here.
/// </remarks>
public sealed class XbgFileTests
{
    [Theory]
    [InlineData(XbgFixtures.Bat)]
    [InlineData(XbgFixtures.Fence)]
    [InlineData(XbmFixtures.Wood)]
    public void Reserialises_byte_for_byte(string fixture)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        Fixture.AssertSameBytes(fixture, original, XbgFile.Parse(original).Write());
    }

    /// <summary>
    /// The AK-47, as a named check that the decode means something rather than merely surviving.
    /// </summary>
    [Fact]
    public void The_rifle_parses_to_the_recorded_shape()
    {
        if (Fixture.Read(XbgFixtures.Ak47) is not { } bytes)
        {
            return;
        }

        XbgFile model = XbgFile.Parse(bytes);

        Assert.Equal(XbgFile.VersionFc2, model.Version);
        Assert.Equal(5, model.Lods.Count);
        Assert.Equal(11, model.Parts.Count);
        Assert.Equal(9, model.Nodes.Count);

        // DIKS carries one entry per part, always.
        Assert.Equal(model.Parts.Count, model.PartRefs.Count);
        Assert.Contains(model.Nodes, node => node.Name == "FX_FIRE");

        // Every part's LOD tier is the _LODn suffix on its own name.
        foreach (XbgPart part in model.Parts)
        {
            int suffix = part.Name.LastIndexOf("_LOD", StringComparison.OrdinalIgnoreCase);
            Assert.True(suffix >= 0, $"{part.Name} carries no _LOD suffix");
            Assert.Equal(int.Parse(part.Name[(suffix + 4)..]), part.Lod);
        }
    }

    /// <summary>
    /// A static cluster's palette is all empty and a skinned one is a contiguous prefix of node
    /// indices then padding - the community rule that a skinned palette never holds -1 is wrong.
    /// </summary>
    [Theory]
    [InlineData(XbgFixtures.Character)]
    [InlineData(XbgFixtures.Prop)]
    public void Bone_palettes_are_a_prefix_then_padding(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        foreach (XbgCluster cluster in XbgFile.Parse(bytes).Parts.SelectMany(part => part.Clusters))
        {
            int used = cluster.Palette.Count(slot => slot != XbgFile.EmptySlot);
            Assert.True(
                cluster.Palette.Take(used).All(slot => slot != XbgFile.EmptySlot)
                && cluster.Palette.Skip(used).All(slot => slot == XbgFile.EmptySlot),
                "palette is not a prefix then padding");
            Assert.True(cluster.IsSkinned || used == 0, $"a static cluster names {used} bones");
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(
            XbgFixtures.Bat, XbgFixtures.Fence, XbgFixtures.Grass, XbgFixtures.Ak47, XbgFixtures.Character,
            XbgFixtures.Prop, XbgFixtures.Buggy, XbgFixtures.SwampBoat, XbgFixtures.FemaleCivilianKit);
}
