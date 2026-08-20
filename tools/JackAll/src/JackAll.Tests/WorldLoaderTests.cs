using System.Collections.Concurrent;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// Which files a world is assembled from. The derivation is a pure path rewrite off the terrain
/// layout, so it can be pinned without any game data behind it.
/// </summary>
public class WorldLoaderTests
{
    /// <summary>
    /// A sector's entities are spread over three files. The worldsector file holds the bulk, but
    /// the two landmark files place the set pieces - a building's roof, its doors and its window
    /// panes are separate meshes, and 72 meshes including every HQ, fort and church are reachable
    /// no other way. Probing only the worldsector file draws those buildings as bare shells.
    /// </summary>
    [Fact]
    public void Every_sector_probes_its_worldsector_file_and_both_landmark_files()
    {
        var probed = new ConcurrentBag<string>();
        var map = new TerrainMap
        {
            Name = "w1_c_3",
            SectorsPerSide = 1,
            Sectors = [(@"worlds\w1_c_3\sdat\sd2592.sdat", 2592)],
        };

        WorldLoader.Load(map, path =>
        {
            probed.Add(path);
            return null;
        });

        Assert.Equal(
            [
                @"worlds\w1_c_3\worldsectors\landmarkfar_2592.data.fcb",
                @"worlds\w1_c_3\worldsectors\landmarknear2592.data.fcb",
                @"worlds\w1_c_3\worldsectors\worldsector2592.data.fcb",
            ],
            probed.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>
    /// The real file behind the fix. Sector 4427 of w2_b_2 keeps a colonial house in its landmark
    /// file: the shell in one entity, the window-and-door panel in another. Alongside them sit the
    /// vegetation container and three spline volumes, which have no geometry of their own and are
    /// drawn by other layers - loading those would put a marker on every sector corner in the map.
    /// </summary>
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void A_landmark_file_contributes_its_buildings_and_nothing_else()
    {
        const string Fixture = @".\Fixtures\WorldSector\landmarknear4427.data.fcb";
        if (!File.Exists(Fixture)) return;

        var map = new TerrainMap
        {
            Name = "w2_b_2",
            SectorsPerSide = 1,
            Sectors = [(@"worlds\w2_b_2\sdat\sd4427.sdat", 4427)],
        };

        Fc2World world = WorldLoader.Load(map, path => path.EndsWith(
            @"landmarknear4427.data.fcb", StringComparison.OrdinalIgnoreCase)
                ? File.ReadAllBytes(Fixture)
                : null);

        Assert.All(world.Entities, e => Assert.StartsWith("StaticObject_", e.Name, StringComparison.Ordinal));
        Assert.All(world.Entities, e => Assert.NotEmpty(WorldModels.MeshPaths(e.Node)));
        Assert.Contains(world.Entities, e => WorldModels.MeshPaths(e.Node)
            .Any(p => p.EndsWith(@"colonialmd01windowsdoors_04.xbg", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(7, world.Entities.Count);

        // The landmark file is not the sector's editable document, so it claims no slot by id.
        Assert.Empty(world.SectorsById);
    }

    /// <summary>A world whose files are all absent still loads, because the paths are probes: a
    /// sector that has no landmark file is the common case, not an error.</summary>
    [Fact]
    public void A_world_with_no_payload_files_loads_empty()
    {
        var map = new TerrainMap
        {
            Name = "w1_c_3",
            SectorsPerSide = 1,
            Sectors = [(@"worlds\w1_c_3\sdat\sd2592.sdat", 2592)],
        };

        Fc2World world = WorldLoader.Load(map, _ => null);

        Assert.Empty(world.Entities);
        Assert.Empty(world.SectorsById);
    }
}
