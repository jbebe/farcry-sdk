using JackAll.Tools.World;

namespace JackAll.Tests;

public class TerrainMapTests
{
    private static IEnumerable<string> SdatPaths(string level, IEnumerable<int> sectorIds)
        => sectorIds.Select(id => $@"levels\{level}\generated\sdat\sd{id}.sdat");

    /// <summary>Campaign cells share one 80x80 sector grid, so all 25 fold into a single map.</summary>
    [Fact]
    public void Campaign_cells_group_into_one_world_on_the_full_grid()
    {
        IEnumerable<string> paths = SdatPaths("w1_a_1", [5120, 6335]).Concat(SdatPaths("w1_e_5", [0, 1279]));

        TerrainMap map = Assert.Single(TerrainMap.Discover(paths));

        Assert.Equal("world1", map.Name);
        Assert.Equal(80, map.SectorsPerSide);
        Assert.Equal(5121, map.GridSide);
        Assert.Equal(4, map.Sectors.Count);
    }

    /// <summary>A multiplayer map is its own 10x10 grid, tmpla an 8x8 one - each inferred from
    /// the largest sector id, with no per-map table.</summary>
    [Theory]
    [InlineData("mp_16_airbase", 99, 10, 641)]
    [InlineData("tmpla", 63, 8, 513)]
    public void Standalone_maps_infer_their_grid_from_the_largest_sector_id(
        string level, int maxSectorId, int expectedSectorsPerSide, int expectedGridSide)
    {
        TerrainMap map = Assert.Single(TerrainMap.Discover(SdatPaths(level, [0, maxSectorId])));

        Assert.Equal(level, map.Name);
        Assert.Equal(expectedSectorsPerSide, map.SectorsPerSide);
        Assert.Equal(expectedGridSide, map.GridSide);
    }

    /// <summary>A modded sector id past the largest grid FC2 ships is dropped, not grown into.</summary>
    [Fact]
    public void Out_of_grid_sector_ids_are_skipped()
    {
        TerrainMap map = Assert.Single(TerrainMap.Discover(SdatPaths("w2_b_2", [12, 14031])));

        Assert.Equal(80, map.SectorsPerSide);
        Assert.Equal(12, Assert.Single(map.Sectors).SectorId);
    }

    [Fact]
    public void Campaign_worlds_are_offered_before_the_standalone_maps()
    {
        IEnumerable<string> paths = SdatPaths("mp_21_town", [99])
            .Concat(SdatPaths("w2_b_2", [12]))
            .Concat(SdatPaths("w1_a_1", [5120]));

        Assert.Equal(["world1", "world2", "mp_21_town"], TerrainMap.Discover(paths).Select(m => m.Name));
    }

    /// <summary>Only cooked sdat under a level counts - the editor slot ships shadow textures with
    /// the same stem and no terrain, and must not surface as a loadable map.</summary>
    [Fact]
    public void Non_terrain_paths_are_ignored()
    {
        string[] paths =
        [
            @"levels\ige_map\generated\sdat\sd12_shadow.xbt",
            @"worlds\world1\generated\entitylibrary.fcb",
            @"levels\w1_a_1\generated\worldsectors\worldsector5120.data.fcb",
        ];

        Assert.Empty(TerrainMap.Discover(paths));
    }
}
