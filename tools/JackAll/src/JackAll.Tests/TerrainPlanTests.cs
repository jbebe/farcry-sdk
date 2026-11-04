using System.Text;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Sdat;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>A new world's terrain plan raised into its flat sector when the world is packed.</summary>
public class TerrainPlanTests
{
    private const string Sector = @"levels\range\generated\sdat\sd0.sdat";
    private const float Ground = 16.0625f;

    // A metre-high ridge 12 m wide along y = 32, from x = 16 to x = 48.
    private const string Plan = """
        <Terrain>
          <Ridge FromX="16" FromY="32" ToX="48" ToY="32" Height="1" Width="12" />
        </Terrain>
        """;

    [Fact]
    public void A_ridge_raises_the_ground_and_tilts_its_slopes()
    {
        if (Pack() is not { } built) return;
        SdatSector sector = SdatSectorFile.Decode(built[Sector]);

        Assert.Equal(Ground + 1, sector.Grid[32, 32].HeightMeters);
        Assert.Equal(Ground + 0.5f, sector.Grid[35, 32].HeightMeters);
        Assert.Equal(Ground, sector.Grid[38, 32].HeightMeters);
        Assert.Equal(Ground, sector.Grid[32, 54].HeightMeters);
        // North of the line the ground falls away north, so its normal leans north; flat ground's is upright.
        Assert.True(sector.NormalYAt(35, 32) > 127);
        Assert.True(sector.NormalYAt(29, 32) < 127);
        Assert.Equal(127, sector.NormalYAt(32, 32));
        Assert.Equal(127, sector.NormalYAt(50, 32));
        Assert.DoesNotContain(TerrainPlan.PathOf("range"), built.Keys);
    }

    [Fact]
    public void The_raised_blocks_keep_every_vertex_and_the_records_reach_the_top()
    {
        if (Pack() is not { } built) return;
        SdatSector sector = SdatSectorFile.Decode(built[Sector]);
        const int side = SdatSectorFile.GridSize;

        // The blocks the ridge raises keep every vertex; elsewhere the coarse grid stays as it was.
        Assert.Equal(3, sector.LastLodAt(33, 33));
        Assert.Equal(3, sector.LastLodAt(24, 10));
        Assert.Equal(0, sector.LastLodAt(49, 33));
        Assert.Equal(2, sector.LastLodAt(44, 44));
        for (int lod = 0; lod < 4; lod++)
        {
            int kept = Enumerable.Range(0, side * side).Count(v => sector.LastLodAt(v / side, v % side) >= lod);
            Assert.Equal(kept, BitConverter.ToUInt16(sector.MaskTablesRaw, SdatSectorFile.LodVertexCountsOffsetInMaskTables + lod * 2));
        }
        for (int record = 0; record < sector.RecordsRaw.Length; record += SdatSectorFile.RecordSize)
        {
            Assert.Equal((ushort)((Ground + 1) * 128), BitConverter.ToUInt16(sector.RecordsRaw, record + 6));
        }
    }

    private static SortedDictionary<string, byte[]>? Pack()
    {
        if (!Fixture.Present("NewWorld/sd0.sdat")) return null;
        Dictionary<string, byte[]> files = new(NewWorld.Create("range", 1, NewWorldTests.Read), StringComparer.OrdinalIgnoreCase)
        {
            [WorldPack.FullLibrary] = FcbDocument.Serialize(new FcbObject { TypeHash = WorldHashes.EntityLibraries }),
            [TerrainPlan.PathOf("range")] = Encoding.UTF8.GetBytes(Plan),
        };
        TerrainMap map = TerrainMap.Discover(files.Keys).Single(m => m.Name == "range");
        return WorldPack.BuildWorld("range", map, files.GetValueOrDefault, files.Keys, FcbClassDefinitions.Empty);
    }
}
