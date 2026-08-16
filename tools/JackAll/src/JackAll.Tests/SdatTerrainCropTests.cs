using JackAll.Tools.Sdat;

namespace JackAll.Tests;

/// <summary>
/// Geometry is checked synthetically; the shared-edge rule is checked against the shipped campaign
/// terrain, where it is the thing that proves a block was assembled correctly.
/// </summary>
public class SdatTerrainCropTests
{
    private static SdatSector Sector(Func<int, int, ushort> height)
    {
        var grid = new SdatGridCell[SdatSectorFile.GridSize, SdatSectorFile.GridSize];
        for (int row = 0; row < SdatSectorFile.GridSize; row++)
        {
            for (int column = 0; column < SdatSectorFile.GridSize; column++)
            {
                grid[row, column] = new SdatGridCell(height(row, column), 0, 0);
            }
        }
        return new SdatSector
        {
            SectorId = 0, Flags = 0, X = 0, Y = 0, UnknownHeaderField = 0, FormatFlag = 1,
            EnvSettingsRaw = [], HeaderPadding = [], Grid = grid,
            MaskTablesRaw = [], RecordsRaw = [], TrailingField = 0, TailBlockRaw = [],
        };
    }

    /// <summary>A block whose height is a function of absolute position, so edges agree by construction.</summary>
    private static SdatSector[,] Continuous(int size)
    {
        var block = new SdatSector[size, size];
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                int originY = row * SdatTerrainCrop.QuadsPerSector;
                int originX = column * SdatTerrainCrop.QuadsPerSector;
                block[row, column] = Sector((r, c) => (ushort)((originY + r) * 1000 + originX + c));
            }
        }
        return block;
    }

    [Theory]
    [InlineData(1, 65)]
    [InlineData(2, 129)]
    [InlineData(8, 513)]
    [InlineData(16, 1025)]
    public void A_block_of_sectors_shares_the_edges_between_them(int sectors, int expectedSide)
    {
        Assert.Equal(expectedSide, SdatTerrainCrop.GridSideFor(sectors));
        Assert.Equal(expectedSide * expectedSide, SdatTerrainCrop.Stitch(Continuous(sectors)).Length);
    }

    [Fact]
    public void An_eight_by_eight_block_fills_the_editors_heightmap_exactly()
    {
        ushort[] heights = SdatTerrainCrop.Stitch(Continuous(8));

        // What ige/heightmap.raw measures in every map the editor writes.
        Assert.Equal(526338, SdatTerrainCrop.ToHeightmapBytes(heights).Length);
    }

    [Fact]
    public void Every_sample_lands_where_its_sector_puts_it()
    {
        ushort[] heights = SdatTerrainCrop.Stitch(Continuous(4));
        int side = SdatTerrainCrop.GridSideFor(4);

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                Assert.Equal((ushort)(y * 1000 + x), heights[y * side + x]);
            }
        }
    }

    [Fact]
    public void Continuous_terrain_has_no_edge_disagreements()
    {
        Assert.Equal(0, SdatTerrainCrop.CountEdgeMismatches(Continuous(8)));
    }

    [Fact]
    public void A_sector_placed_out_of_order_is_reported()
    {
        SdatSector[,] block = Continuous(2);
        (block[0, 0], block[0, 1]) = (block[0, 1], block[0, 0]);

        Assert.True(SdatTerrainCrop.CountEdgeMismatches(block) > 0);
    }

    [Fact]
    public void A_non_square_block_is_refused()
    {
        Assert.Throws<ArgumentException>(() => SdatTerrainCrop.Stitch(new SdatSector[2, 3]));
    }
}
