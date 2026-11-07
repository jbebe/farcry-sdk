using System.Numerics;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>What a ring around a sector reaches, which is everything the map's model streams draw.</summary>
public class SectorIndexTests
{
    private static Vector3 In(int sectorX, int sectorY)
        => new((sectorX + 0.5f) * WorldModels.SectorMeters, (sectorY + 0.5f) * WorldModels.SectorMeters, 0f);

    [Fact]
    public void A_ring_reaches_the_square_of_sectors_around_its_centre_and_no_further()
    {
        string[] names = ["centre", "edge", "corner", "outside"];
        Vector3[] positions = [In(10, 10), In(11, 10), In(9, 11), In(12, 10)];
        SectorIndex<string> index = SectorIndex<string>.Of(names, n => positions[Array.IndexOf(names, n)]);

        Dictionary<string, int> reached = index.Ring((10, 10), 1)
            .SelectMany(s => s.Items.Select(name => (name, s.Distance)))
            .ToDictionary(p => p.name, p => p.Distance);

        Assert.Equal(new Dictionary<string, int> { ["centre"] = 0, ["edge"] = 1, ["corner"] = 1 }, reached);
    }

    [Fact]
    public void A_removed_item_leaves_its_sector_and_can_be_filed_under_another()
    {
        var index = new SectorIndex<string>();
        index.Add((3, 4), "jeep");

        Assert.True(index.Remove((3, 4), "jeep"));
        Assert.False(index.Remove((3, 4), "jeep"));
        index.Add((5, 4), "jeep");

        Assert.Empty(index.Ring((3, 4), 0).SelectMany(s => s.Items));
        Assert.Equal("jeep", Assert.Single(index.Ring((5, 4), 0).SelectMany(s => s.Items)));
    }
}
