namespace JackAll.Tools.Sdat;

/// <summary>
/// Stitches a square block of terrain sectors into the single height grid the in-game editor
/// authors from (<c>ige/heightmap.raw</c>).
///
/// Sectors are 65x65 vertices covering 64 quads, so neighbours share their touching edge: an NxN
/// block yields N*64+1 vertices a side. An 8x8 block gives 513, which is exactly what the editor's
/// 512-unit world uses.
/// </summary>
/// <remarks>
/// The editor cooks <c>heightmap.raw</c> straight into the per-sector grids with no scaling - a
/// sample equals its sector cell's <see cref="SdatGridCell.RawHeight"/> - so retail terrain can be
/// carried across verbatim.
/// </remarks>
public static class SdatTerrainCrop
{
    public const int QuadsPerSector = SdatSectorFile.GridSize - 1;

    /// <summary>Vertices a side for a block <paramref name="sectors"/> sectors across.</summary>
    public static int GridSideFor(int sectors) => sectors * QuadsPerSector + 1;

    /// <summary>
    /// Raw height samples, row-major, for a <c>[row, col]</c> block of sectors. Shared edges are
    /// written by both neighbours; use <see cref="CountEdgeMismatches"/> to confirm they agree.
    /// </summary>
    public static ushort[] Stitch(SdatSector[,] block)
    {
        int rows = block.GetLength(0);
        int columns = block.GetLength(1);
        if (rows != columns)
        {
            throw new ArgumentException("The sector block must be square.", nameof(block));
        }

        int side = GridSideFor(rows);
        var heights = new ushort[side * side];
        for (int sectorRow = 0; sectorRow < rows; sectorRow++)
        {
            for (int sectorColumn = 0; sectorColumn < columns; sectorColumn++)
            {
                SdatGridCell[,] grid = block[sectorRow, sectorColumn].Grid;
                for (int row = 0; row < SdatSectorFile.GridSize; row++)
                {
                    for (int column = 0; column < SdatSectorFile.GridSize; column++)
                    {
                        int y = sectorRow * QuadsPerSector + row;
                        int x = sectorColumn * QuadsPerSector + column;
                        heights[y * side + x] = grid[row, column].RawHeight;
                    }
                }
            }
        }
        return heights;
    }

    /// <summary>
    /// Samples where neighbouring sectors disagree along an edge they share. Zero on well-formed
    /// terrain, so a non-zero count means the block was assembled from the wrong sectors or in the
    /// wrong order.
    /// </summary>
    public static int CountEdgeMismatches(SdatSector[,] block)
    {
        int rows = block.GetLength(0);
        int columns = block.GetLength(1);
        int last = SdatSectorFile.GridSize - 1;
        int mismatches = 0;

        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column + 1 < columns; column++)
            {
                SdatGridCell[,] left = block[row, column].Grid;
                SdatGridCell[,] right = block[row, column + 1].Grid;
                for (int i = 0; i < SdatSectorFile.GridSize; i++)
                {
                    if (left[i, last].RawHeight != right[i, 0].RawHeight)
                    {
                        mismatches++;
                    }
                }
            }
        }

        for (int row = 0; row + 1 < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                SdatGridCell[,] top = block[row, column].Grid;
                SdatGridCell[,] bottom = block[row + 1, column].Grid;
                for (int i = 0; i < SdatSectorFile.GridSize; i++)
                {
                    if (top[last, i].RawHeight != bottom[0, i].RawHeight)
                    {
                        mismatches++;
                    }
                }
            }
        }
        return mismatches;
    }

    /// <summary>Little-endian bytes, the form <c>ige/heightmap.raw</c> stores.</summary>
    public static byte[] ToHeightmapBytes(ushort[] heights)
    {
        var raw = new byte[heights.Length * 2];
        for (int i = 0; i < heights.Length; i++)
        {
            raw[i * 2] = (byte)heights[i];
            raw[i * 2 + 1] = (byte)(heights[i] >> 8);
        }
        return raw;
    }
}
