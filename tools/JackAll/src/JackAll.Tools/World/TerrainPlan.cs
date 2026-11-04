using System.Numerics;
using System.Xml.Linq;
using JackAll.Tools.Sdat;
using static JackAll.Tools.World.PlanXml;

namespace JackAll.Tools.World;

/// <summary>
/// A new world's ground as its author shapes it, raised into the sectors' terrain when the world is
/// packed, before its vegetation and navmesh are laid on it. A ridge lifts the ground along a line by
/// its height and eases back down to it over half its width either side, round at its ends:
/// <code>
/// &lt;Terrain&gt;
///   &lt;Ridge FromX="272" FromY="376" ToX="352" ToY="376" Height="1" Width="12" /&gt;
/// &lt;/Terrain&gt;
/// </code>
/// </summary>
/// <remarks>
/// A raised vertex is kept in every LOD mesh, block by block, so the ground a soldier's ray meets is
/// the ground drawn at any distance. The blocks around stay as simplified as before; where they meet
/// there is no crack only while the unraised ground there is flat, as a new world's is.
/// </remarks>
public static class TerrainPlan
{
    /// <summary>The side of the coarsest LOD's quads, the blocks a raise keeps whole.</summary>
    private const int LodBlock = 8;

    public static string PathOf(string level) => $@"levels\{level}\terrain.xml";

    /// <summary>The terrain file of every sector the plan changes, by path.</summary>
    public static Dictionary<string, byte[]> Build(XElement plan, TerrainMap map, Func<string, byte[]?> read)
    {
        Ridge[] ridges = [.. plan.Elements("Ridge").Select(Ridge.Parse)];
        WorldTerrain ground = WorldTerrain.Load(map, read);
        float Raise(int x, int y) => ridges.Sum(r => r.At(x, y));
        float Height(int x, int y) => ground.HeightMetersAt(x, y) + Raise(x, y);

        const int side = SdatSectorFile.GridSize;
        var shaped = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, int id) in map.Sectors)
        {
            int x0 = id % map.SectorsPerSide * SdatTerrainCrop.QuadsPerSector;
            int y0 = id / map.SectorsPerSide * SdatTerrainCrop.QuadsPerSector;
            SdatSector? sector = null;
            for (int row = 0; row < side; row++)
            {
                for (int column = 0; column < side; column++)
                {
                    int x = x0 + column, y = y0 + row;
                    // A vertex beside a raised one keeps its height but tilts with it.
                    if (Raise(x, y) == 0 && Raise(x - 1, y) == 0 && Raise(x + 1, y) == 0 && Raise(x, y - 1) == 0 && Raise(x, y + 1) == 0)
                    {
                        continue;
                    }
                    sector ??= SdatSectorFile.Decode(read(path) ?? throw new InvalidOperationException($"{path} is missing."));
                    float gx = (Height(x + 1, y) - Height(x - 1, y)) / 2, gy = (Height(x, y + 1) - Height(x, y - 1)) / 2;
                    float length = MathF.Sqrt(gx * gx + gy * gy + 1);
                    ushort raw = (ushort)Math.Clamp(MathF.Round(Height(x, y) / SdatSectorFile.MetersPerUnit), 0, ushort.MaxValue);
                    sector.SetCell(row, column, raw, -gx / length, -gy / length);
                }
            }
            if (sector is null)
            {
                continue;
            }
            for (int blockRow = 0; blockRow < side - 1; blockRow += LodBlock)
            {
                for (int blockColumn = 0; blockColumn < side - 1; blockColumn += LodBlock)
                {
                    if (Block(blockRow, blockColumn).Any(v => Raise(x0 + v.Column, y0 + v.Row) != 0))
                    {
                        foreach ((int row, int column) in Block(blockRow, blockColumn))
                        {
                            sector.KeepAtEveryLod(row, column);
                        }
                    }
                }
            }
            sector.RefreshLodSummaries();
            shaped[path] = SdatSectorFile.Encode(sector);
        }
        return shaped;
    }

    /// <summary>Every vertex of the block whose first corner is at (<paramref name="row"/>, <paramref name="column"/>), its edges included.</summary>
    private static IEnumerable<(int Row, int Column)> Block(int row, int column)
        => from r in Enumerable.Range(row, LodBlock + 1) from c in Enumerable.Range(column, LodBlock + 1) select (r, c);

    private sealed record Ridge(Vector2 From, Vector2 To, float Height, float HalfWidth)
    {
        public static Ridge Parse(XElement ridge) => new(
            new Vector2(Number(ridge, "FromX"), Number(ridge, "FromY")),
            new Vector2(Number(ridge, "ToX"), Number(ridge, "ToY")),
            Number(ridge, "Height"), Number(ridge, "Width") / 2);

        /// <summary>How far the ridge lifts the ground at a point: its height on the line, nothing half its width from it.</summary>
        public float At(int x, int y)
        {
            Vector2 point = new(x, y), along = To - From;
            float share = along == Vector2.Zero ? 0 : Math.Clamp(Vector2.Dot(point - From, along) / along.LengthSquared(), 0, 1);
            float off = Vector2.Distance(point, From + along * share);
            return off >= HalfWidth ? 0 : Height * (1 + MathF.Cos(MathF.PI * off / HalfWidth)) / 2;
        }
    }
}
