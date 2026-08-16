using JackAll.Tools.World;

namespace JackAll.Tests;

public class WorldGridTests
{
    [Fact]
    public void Sector_coords_round_trip_through_ids()
    {
        Assert.Equal((36, 30), Fc2WorldGrid.SectorCoords(2910));
        Assert.Equal(2910, Fc2WorldGrid.SectorId(36, 30));
        Assert.Equal(2910, Fc2WorldGrid.SectorIdAt(30 * 64f + 32f, 36 * 64f + 32f));
    }

    [Fact]
    public void World_names_map_to_their_cell_digit()
    {
        Assert.Equal("1", Fc2WorldGrid.WorldDigit("world1"));
        Assert.Equal("2", Fc2WorldGrid.WorldDigit("world2"));
        Assert.Throws<ArgumentException>(() => Fc2WorldGrid.WorldDigit("ige_map"));
    }
}
