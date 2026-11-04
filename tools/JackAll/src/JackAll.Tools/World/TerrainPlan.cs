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
/// Each 8x8 block a ridge raises keeps every vertex at every LOD, and the blocks around stay as
/// simplified as they were, which meets them without a crack only on flat ground: a sector whose LODs
/// leave out detail is refused.
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
        const int side = SdatSectorFile.GridSize;
        var shaped = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, int id) in map.Sectors)
        {
            int x0 = id % map.SectorsPerSide * SdatTerrainCrop.QuadsPerSector;
            int y0 = id / map.SectorsPerSide * SdatTerrainCrop.QuadsPerSector;
            Ridge[] near = [.. ridges.Where(r => r.Reaches(x0 - 1, y0 - 1, x0 + side, y0 + side))];
            if (near.Length == 0)
            {
                continue;
            }

            // The raise over the sector's vertices and the ring around them its slopes lean on.
            var raise = new float[side + 2, side + 2];
            for (int row = -1; row <= side; row++)
            {
                for (int column = -1; column <= side; column++)
                {
                    raise[row + 1, column + 1] = near.Sum(r => r.At(x0 + column, y0 + row));
                }
            }
            float Raise(int row, int column) => raise[row + 1, column + 1];
            float Height(int row, int column) => ground.HeightMetersAt(x0 + column, y0 + row) + Raise(row, column);

            SdatSector? sector = null;
            for (int row = 0; row < side; row++)
            {
                for (int column = 0; column < side; column++)
                {
                    // A vertex beside a raised one keeps its height but tilts with it.
                    if (Raise(row, column) == 0 && Raise(row, column - 1) == 0 && Raise(row, column + 1) == 0 &&
                        Raise(row - 1, column) == 0 && Raise(row + 1, column) == 0)
                    {
                        continue;
                    }
                    sector ??= SdatSectorFile.Decode(read(path) ?? throw new InvalidOperationException($"{path} is missing."));
                    float gx = (Height(row, column + 1) - Height(row, column - 1)) / 2;
                    float gy = (Height(row + 1, column) - Height(row - 1, column)) / 2;
                    float length = MathF.Sqrt(gx * gx + gy * gy + 1);
                    sector.SetCell(row, column, Height(row, column), -gx / length, -gy / length);
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
                    if (!Raised(blockRow, blockColumn))
                    {
                        continue;
                    }
                    if (sector.TailBlockRaw.Any(b => b != 0))
                    {
                        throw new InvalidOperationException($"{path}'s distant LODs leave out detail, so a ridge cannot be raised there: only flat ground can.");
                    }
                    for (int row = blockRow; row <= blockRow + LodBlock; row++)
                    {
                        for (int column = blockColumn; column <= blockColumn + LodBlock; column++)
                        {
                            sector.KeepAtEveryLod(row, column);
                        }
                    }
                }
            }
            sector.RefreshLodSummaries();
            shaped[path] = SdatSectorFile.Encode(sector);

            bool Raised(int blockRow, int blockColumn)
            {
                for (int row = blockRow; row <= blockRow + LodBlock; row++)
                {
                    for (int column = blockColumn; column <= blockColumn + LodBlock; column++)
                    {
                        if (Raise(row, column) != 0)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }
        return shaped;
    }

    private sealed record Ridge(Vector2 From, Vector2 To, float Height, float HalfWidth)
    {
        public static Ridge Parse(XElement ridge) => new(
            new Vector2(Number(ridge, "FromX"), Number(ridge, "FromY")),
            new Vector2(Number(ridge, "ToX"), Number(ridge, "ToY")),
            Number(ridge, "Height"), Number(ridge, "Width") / 2);

        /// <summary>Whether the ridge can lift anything in the rectangle.</summary>
        public bool Reaches(int minX, int minY, int maxX, int maxY)
            => MathF.Min(From.X, To.X) - HalfWidth < maxX && MathF.Max(From.X, To.X) + HalfWidth > minX &&
               MathF.Min(From.Y, To.Y) - HalfWidth < maxY && MathF.Max(From.Y, To.Y) + HalfWidth > minY;

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
