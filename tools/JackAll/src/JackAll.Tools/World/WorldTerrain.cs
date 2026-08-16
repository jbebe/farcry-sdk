using JackAll.Tools.Sdat;

namespace JackAll.Tools.World;

/// <summary>
/// A map's terrain heights: every <c>sd&lt;id&gt;.sdat</c> sector stitched into one grid, one sample
/// per world unit, row-major <c>[y * Side + x]</c> with grid row = world Y. Raw values are the sdat
/// encoding: meters * 128. A missing sector leaves its samples zero - flat terrain is the honest
/// rendering for a map that ships no file for that cell.
/// </summary>
public sealed class WorldTerrain
{
    /// <summary>Height samples a side.</summary>
    public int Side { get; }

    public ushort[] Heights { get; }

    /// <summary>Raw-height extremes across the map, for normalizing a relief rendering.</summary>
    public ushort MinHeight { get; }
    public ushort MaxHeight { get; }

    private WorldTerrain(int side, ushort[] heights, ushort min, ushort max)
    {
        Side = side;
        Heights = heights;
        MinHeight = min;
        MaxHeight = Math.Max(max, (ushort)(min + 1));
    }

    public float HeightMetersAt(int x, int y)
        => Heights[Math.Clamp(y, 0, Side - 1) * Side + Math.Clamp(x, 0, Side - 1)]
           * (float)SdatSectorFile.MetersPerUnit;

    public static WorldTerrain Load(
        TerrainMap map,
        Func<string, byte[]?> readByPath,
        IProgress<string>? progress = null)
    {
        progress?.Report($"Loading {map.Name} terrain: {map.Sectors.Count} sectors");

        // Each decoded sector's heights stream straight into the stitched grid (shared edges simply
        // written by both neighbours), so no sector outlives its own copy loop.
        int side = map.GridSide;
        var heights = new ushort[side * side];
        ushort min = ushort.MaxValue, max = 0;
        object minMaxLock = new();
        Parallel.ForEach(map.Sectors, item =>
        {
            if (readByPath(item.Path) is not { } data)
            {
                return;
            }

            SdatGridCell[,] grid = SdatSectorFile.Decode(data).Grid;
            int y0 = item.SectorId / map.SectorsPerSide * SdatTerrainCrop.QuadsPerSector;
            int x0 = item.SectorId % map.SectorsPerSide * SdatTerrainCrop.QuadsPerSector;
            ushort localMin = ushort.MaxValue, localMax = 0;
            for (int gy = 0; gy < SdatSectorFile.GridSize; gy++)
            {
                int rowBase = (y0 + gy) * side + x0;
                for (int gx = 0; gx < SdatSectorFile.GridSize; gx++)
                {
                    ushort h = grid[gy, gx].RawHeight;
                    heights[rowBase + gx] = h;
                    if (h < localMin) localMin = h;
                    if (h > localMax) localMax = h;
                }
            }

            lock (minMaxLock)
            {
                if (localMin < min) min = localMin;
                if (localMax > max) max = localMax;
            }
        });

        return new WorldTerrain(side, heights, min == ushort.MaxValue ? (ushort)0 : min, max);
    }
}
