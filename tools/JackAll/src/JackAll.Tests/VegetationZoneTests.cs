using System.Numerics;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>A landmark file's nine vegetation lists, read as resources, clusters and tufts and written back.</summary>
public class VegetationZoneTests
{
    // A multiplayer savannah sector: one grass mesh, one zone.
    public const string Savanna = "WorldSector/landmarknear27.data.fcb";

    // A campaign sector whose editor put 259 tufts in its cluster 33, past the count byte.
    public const string Overfilled = "WorldSector/landmarknear2836.data.fcb";

    // A campaign far file: five RealTrees, an acacia among them, each cluster saved without a sphere.
    public const string Trees = "WorldSector/landmarkfar_3663.data.fcb";

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent(Savanna, Overfilled, Trees, WorldSectorFragmentTests.Landmark4427);

    /// <summary>Every list comes back byte for byte: of 12,203 shipped zones, all but the 6 the editor overfilled.</summary>
    [Theory]
    [InlineData(Savanna)]
    [InlineData(Trees)]
    [InlineData(WorldSectorFragmentTests.Landmark4427)]
    public void A_shipped_zone_writes_back_byte_for_byte(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes) return;
        FcbObject[] zones = [.. FcbDocument.Deserialize(bytes).Descendants().Where(n => n.TypeHash == VegetationZone.TypeHash)];

        Assert.NotEmpty(zones);
        foreach (FcbObject zone in zones)
        {
            FcbObject back = VegetationZone.Read(zone).ToFcb();
            Assert.Equal(zone.Values.Keys.Order(), back.Values.Keys.Order());
            Assert.All(zone.Values, v => Assert.Equal(v.Value, back.Values[v.Key]));
        }
    }

    /// <summary>
    /// A cluster's count is its packed word's low byte, so 259 tufts carry into the offset: the
    /// engine draws 3, from one tuft further on, and so does the reader.
    /// </summary>
    [Fact]
    public void An_overfilled_cluster_reads_as_the_engine_draws_it()
    {
        if (Fixture.Read(Overfilled) is not { } bytes) return;
        FcbObject node = FcbDocument.Deserialize(bytes).Descendants().First(n => n.TypeHash == VegetationZone.TypeHash);
        VegetationCluster[] clusters = [.. VegetationZone.Read(node).Resources.SelectMany(r => r.Clusters)];

        Assert.Equal(3, clusters[33].Tufts.Count);
        Assert.True(FcbValueCodec.TryDecode(FcbMemberType.UInt32Array, node.Values[FcbClassDefinitions.Crc32Ascii("posXYList")], out object xy));
        Assert.Equal((((uint[])xy)[501] & 0xFFFF) / 10f, clusters[33].Tufts[0].Position.X);
        Assert.Equal(46, clusters[34].Tufts.Count);
    }

    [Fact]
    public void Turns_and_normals_pack_as_the_editor_packs_them()
    {
        Assert.Equal(0x0000_7FFFu, VegetationTuft.PackTurn(0));
        Assert.Equal(0x007F_7FFFu, VegetationTuft.PackNormal(Vector3.UnitZ));
        foreach (float turn in new[] { 0.5f, 2f, -2.5f })
        {
            Assert.Equal(turn, new VegetationTuft(default, VegetationTuft.PackTurn(turn), 0, 0, 0).Turn, 3);
        }
    }
}
