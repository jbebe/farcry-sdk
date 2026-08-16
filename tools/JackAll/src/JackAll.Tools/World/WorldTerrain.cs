using JackAll.Tools.Sdat;

namespace JackAll.Tools.World;

/// <summary>
/// A whole SP world's terrain heights: all 6400 <c>sd&lt;id&gt;.sdat</c> sectors (dense, ids 0-6399
/// on the 80x80 grid in both shipped worlds) stitched into one grid, one sample per world unit,
/// row-major <c>[y * Side + x]</c> with grid row = world Y. Raw values are the sdat encoding:
/// meters * 128. A missing sector leaves its samples zero - flat terrain is the honest rendering
/// for a modded/partial install.
/// </summary>
public sealed class WorldTerrain
{
    public static readonly int Side = SdatTerrainCrop.GridSideFor(Fc2WorldGrid.WorldSide);

    public ushort[] Heights { get; }

    /// <summary>Raw-height extremes across the world, for normalizing a relief rendering.</summary>
    public ushort MinHeight { get; }
    public ushort MaxHeight { get; }

    internal WorldTerrain(ushort[] heights, ushort min, ushort max)
    {
        Heights = heights;
        MinHeight = min;
        MaxHeight = Math.Max(max, (ushort)(min + 1));
    }

    public float HeightMetersAt(int x, int y)
        => Heights[Math.Clamp(y, 0, Side - 1) * Side + Math.Clamp(x, 0, Side - 1)]
           * (float)SdatSectorFile.MetersPerUnit;

    public static WorldTerrain Load(
        WorldPaths paths,
        Func<string, byte[]?> readByPath,
        IProgress<string>? progress = null)
    {
        progress?.Report($"Loading {paths.WorldName} terrain: {paths.Terrain.Count} sectors");

        // Each decoded sector's heights stream straight into the stitched grid (shared edges simply
        // written by both neighbours), so no sector outlives its own copy loop.
        var heights = new ushort[Side * Side];
        ushort min = ushort.MaxValue, max = 0;
        object minMaxLock = new();
        Parallel.ForEach(paths.Terrain, item =>
        {
            if (readByPath(item.Path) is not { } data)
            {
                return;
            }

            SdatGridCell[,] grid = SdatSectorFile.Decode(data).Grid;
            (int row, int col) = Fc2WorldGrid.SectorCoords(item.SectorId);
            int y0 = row * SdatTerrainCrop.QuadsPerSector;
            int x0 = col * SdatTerrainCrop.QuadsPerSector;
            ushort localMin = ushort.MaxValue, localMax = 0;
            for (int gy = 0; gy < SdatSectorFile.GridSize; gy++)
            {
                int rowBase = (y0 + gy) * Side + x0;
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

        return new WorldTerrain(heights, min == ushort.MaxValue ? (ushort)0 : min, max);
    }
}
