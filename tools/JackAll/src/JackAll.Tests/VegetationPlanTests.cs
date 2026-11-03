using System.Numerics;
using System.Text;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>A new world's vegetation plan grown into landmark files when the world is packed.</summary>
public class VegetationPlanTests
{
    private const string Mesh = @"graphics\vegetation\savannah\1_grasssavannah_b_mp.xbg";
    private const string Donor = @"levels\mp_11_l_savanna\generated\worldsectors\landmarknear27.data.fcb";
    private const string Grown = @"levels\range\generated\worldsectors\landmarknear0.data.fcb";
    // A multiplayer world's collection manager, which lists the MP savannah grass as hiding no one.
    private const string Managers = "World/mp_17_dunes.managers.fcb";

    // A field, and one cluster's square planted four times as thick as a cluster holds.
    private const string Plan = $"""
        <Vegetation>
          <Patch Mesh="{Mesh}" Donor="mp_11_l_savanna" Seed="7">
            <Area X="8" Y="8" Width="40" Height="20" />
          </Patch>
          <Patch Mesh="{Mesh}" Donor="mp_11_l_savanna" Spacing="0.5">
            <Area X="16" Y="32" Width="16" Height="16" />
          </Patch>
        </Vegetation>
        """;

    [Fact]
    public void A_plan_grows_clustered_tufts_dressed_like_the_donors()
    {
        if (Pack() is not { } built) return;
        VegetationZone zone = WorldVegetation.Zones(FcbDocument.Deserialize(built[Grown])).Single();
        VegetationResource grass = zone.Resources.Single();
        VegetationTuft[] tufts = [.. grass.Clusters.SelectMany(c => c.Tufts)];
        HashSet<(uint, uint)> looks = [.. DonorTufts().Select(t => (t.Colour, t.Sky))];

        Assert.Equal(FcbClassDefinitions.Crc32Ascii(Mesh), grass.Id);
        Assert.InRange(tufts.Count(t => t.Position.Y < 30), 25 * 12, 25 * 13);
        Assert.InRange(tufts.Count(t => t.Position.Y >= 32), 32 * 32 - 64, 32 * 32);
        Assert.All(tufts, t => Assert.Contains((t.Colour, t.Sky), looks));
        Assert.All(tufts, t => Assert.Equal(16.0625f, t.Position.Z));
        Assert.All(grass.Clusters, c =>
        {
            Assert.InRange(c.Tufts.Count, 1, VegetationCluster.MaxTufts);
            Assert.Single(c.Tufts.Select(t => ((int)(t.Position.X / VegetationPlan.ClusterSide), (int)(t.Position.Y / VegetationPlan.ClusterSide))).Distinct());
            Assert.All(c.Tufts, t => Assert.True(Vector3.Distance(t.Position, new Vector3(c.Sphere.X, c.Sphere.Y, c.Sphere.Z)) < c.Sphere.W));
        });
        // The thick square fills clusters to the limit before it starts another.
        int[] dense = [.. grass.Clusters.Where(c => c.Tufts[0].Position.Y >= 32).Select(c => c.Tufts.Count)];
        Assert.True(dense.Length > 1);
        Assert.All(dense[..^1], count => Assert.Equal(VegetationCluster.MaxTufts, count));
    }

    [Fact]
    public void A_grown_sector_is_flagged_and_shaped_like_a_shipped_landmark()
    {
        if (Pack() is not { } built || Fixture.Read(VegetationZoneTests.Savanna) is not { } shipped) return;

        FcbObject entry = FcbDocument.Deserialize(built[@"worlds\range\generated\range.sectorsdep.fcb"]).Children.Single().Children.Single();
        Assert.Equal([1], entry.Values[FcbClassDefinitions.Crc32Ascii(SectorsDep.HasLandmarkNear)]);
        Assert.DoesNotContain(VegetationPlan.PathOf("range"), built.Keys);

        // Everything but the fire a shipped sector carries, the zone's lists and its own identity.
        Assert.Equal(Shape(FcbDocument.Deserialize(shipped)), Shape(FcbDocument.Deserialize(built[Grown])));
    }

    /// <summary>A mesh the world's collection manager does not list would never be drawn, so packing refuses it.</summary>
    [Fact]
    public void A_mesh_the_world_does_not_list_is_refused()
    {
        if (!Present) return;
        string unlisted = Plan.Replace("savannah\\1_grasssavannah_b_mp.xbg", "savannah\\1_grasssavannah_unlisted.xbg");
        Assert.Contains("collection manager has no", Assert.Throws<InvalidOperationException>(() => Pack(unlisted)).Message);
    }

    private static bool Present => Fixture.Present("NewWorld/sd0.sdat", XbgFixtures.Grass, VegetationZoneTests.Savanna, Managers);

    private static SortedDictionary<string, byte[]>? Pack(string plan = Plan)
    {
        if (!Present) return null;
        Dictionary<string, byte[]> files = new(NewWorld.Create("range", 1, NewWorldTests.Read), StringComparer.OrdinalIgnoreCase)
        {
            [WorldPack.FullLibrary] = FcbDocument.Serialize(new FcbObject { TypeHash = WorldHashes.EntityLibraries }),
            [@"worlds\range\generated\range.managers.fcb"] = Fixture.Read(Managers)!,
            [VegetationPlan.PathOf("range")] = Encoding.UTF8.GetBytes(plan),
            [Mesh] = Fixture.Read(XbgFixtures.Grass)!,
            [Donor] = Fixture.Read(VegetationZoneTests.Savanna)!,
        };
        TerrainMap map = TerrainMap.Discover(files.Keys).Single(m => m.Name == "range");
        return WorldPack.BuildWorld("range", map, files.GetValueOrDefault, files.Keys, FcbClassDefinitions.Empty);
    }

    private static IEnumerable<VegetationTuft> DonorTufts()
        => WorldVegetation.Zones(FcbDocument.Deserialize(Fixture.Read(VegetationZoneTests.Savanna)!))
            .SelectMany(z => z.Resources).SelectMany(r => r.Clusters).SelectMany(c => c.Tufts);

    private static readonly HashSet<uint> Identity =
    [
        WorldHashes.WorldSectorId, WorldHashes.WorldSectorX, WorldHashes.WorldSectorY, WorldHashes.HidName,
        WorldHashes.DisEntityId, WorldHashes.HidPos, WorldHashes.HidPosPrecise,
        FcbClassDefinitions.Crc32Ascii("bboxMin"), FcbClassDefinitions.Crc32Ascii("bboxMax"),
    ];

    private static readonly uint Fire = FcbClassDefinitions.Crc32Ascii("CCollectionIgnitorComponent");

    /// <summary>Every node's type and values, depth first, with the fire, the lists and the identity left out.</summary>
    private static List<string> Shape(FcbObject root)
        => [.. Unburnt(root).Select(n => $"{n.TypeHash:X8}: " + (n.TypeHash == VegetationZone.TypeHash ? "" : string.Join(" ", n.Values
            .Where(v => !Identity.Contains(v.Key))
            .OrderBy(v => v.Key)
            .Select(v => $"{v.Key:X8}={Convert.ToHexString(v.Value)}"))))];

    private static IEnumerable<FcbObject> Unburnt(FcbObject node)
        => node.TypeHash == Fire ? [] : node.Children.SelectMany(Unburnt).Prepend(node);
}
