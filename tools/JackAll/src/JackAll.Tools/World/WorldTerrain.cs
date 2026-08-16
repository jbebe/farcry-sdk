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

    /// <summary>
    /// Each sample's surface type, resolved through its own sector's palette so the values are
    /// comparable across the map (the raw per-cell slot number is not). <c>0xFF</c> means a hole or
    /// a sector that shipped no terrain.
    /// </summary>
    public byte[] SurfaceTypes { get; }

    /// <summary>Surface types present, most ground covered first.</summary>
    public IReadOnlyList<(byte SurfaceType, long Samples)> SurfaceTypeCoverage { get; }

    /// <summary>Raw-height extremes across the map, for normalizing a relief rendering.</summary>
    public ushort MinHeight { get; }
    public ushort MaxHeight { get; }

    private WorldTerrain(int side, ushort[] heights, byte[] surfaceTypes, long[] coverage, ushort min, ushort max)
    {
        Side = side;
        Heights = heights;
        SurfaceTypes = surfaceTypes;
        MinHeight = min;
        MaxHeight = Math.Max(max, (ushort)(min + 1));
        SurfaceTypeCoverage = [.. Enumerable.Range(0, coverage.Length)
            .Where(i => coverage[i] > 0)
            .OrderByDescending(i => coverage[i])
            .Select(i => ((byte)i, coverage[i]))];
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
        var surfaceTypes = new byte[side * side];
        Array.Fill(surfaceTypes, (byte)0xFF);
        var coverage = new long[256];
        ushort min = ushort.MaxValue, max = 0;
        object mergeLock = new();
        Parallel.ForEach(map.Sectors, item =>
        {
            if (readByPath(item.Path) is not { } data)
            {
                return;
            }

            SdatSector sector = SdatSectorFile.Decode(data);
            int y0 = item.SectorId / map.SectorsPerSide * SdatTerrainCrop.QuadsPerSector;
            int x0 = item.SectorId % map.SectorsPerSide * SdatTerrainCrop.QuadsPerSector;
            ushort localMin = ushort.MaxValue, localMax = 0;
            var localCoverage = new long[256];
            for (int gy = 0; gy < SdatSectorFile.GridSize; gy++)
            {
                int rowBase = (y0 + gy) * side + x0;
                for (int gx = 0; gx < SdatSectorFile.GridSize; gx++)
                {
                    ushort h = sector.Grid[gy, gx].RawHeight;
                    heights[rowBase + gx] = h;
                    if (h < localMin) localMin = h;
                    if (h > localMax) localMax = h;

                    // A sector's grid is 65x65 vertices over 64x64 quads, and the surface type is a
                    // quad attribute - the trailing row and column carry no quad and read back as
                    // slot 0. Clamping to the last real quad stops that showing as a one-sample
                    // line of the wrong surface along every sector boundary.
                    byte surface = sector.SurfaceTypeAt(Math.Min(gy, SdatSectorFile.GridSize - 2),
                        Math.Min(gx, SdatSectorFile.GridSize - 2));
                    surfaceTypes[rowBase + gx] = surface;
                    localCoverage[surface]++;
                }
            }

            lock (mergeLock)
            {
                if (localMin < min) min = localMin;
                if (localMax > max) max = localMax;
                for (int i = 0; i < localCoverage.Length; i++)
                {
                    coverage[i] += localCoverage[i];
                }
            }
        });

        return new WorldTerrain(side, heights, surfaceTypes, coverage, min == ushort.MaxValue ? (ushort)0 : min, max);
    }
}
