using System.Numerics;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>The ground a navmesh stands on: terrain heights one sample a metre, and the quads cut out of it.</summary>
/// <param name="Height">The height of the sample at a whole-metre world position.</param>
/// <param name="Hole">Whether the quad whose lower corner is at a whole-metre position is a hole.</param>
public sealed record NavMeshTerrain(Func<int, int, float> Height, Func<int, int, bool> Hole)
{
    public static NavMeshTerrain Of(WorldTerrain terrain) => new(
        terrain.HeightMetersAt,
        (x, y) => x < 0 || y < 0 || x >= terrain.Side || y >= terrain.Side || terrain.SurfaceTypes[y * terrain.Side + x] == 0xFF);

    public static NavMeshTerrain Flat(float height) => new((_, _) => height, (_, _) => false);
}

/// <summary>
/// What may be walked, and how finely it is sampled, in metres and degrees. The mesh is not shrunk
/// away from walls: like the engine's own, it runs up to them and leaves agent size to the clearance radii.
/// </summary>
public sealed record NavMeshSettings
{
    public float CellSize { get; init; } = 0.25f;
    public float CellHeight { get; init; } = 0.05f;

    /// <summary>The headroom a surface needs to be walked on.</summary>
    public float AgentHeight { get; init; } = 1.6f;

    /// <summary>The highest step walked up without a ramp.</summary>
    public float MaxClimb { get; init; } = 0.3f;

    /// <summary>The engine generator's default walkable slope.</summary>
    public float MaxSlope { get; init; } = 45f;

    /// <summary>The side of the smallest area kept: the engine generator drops platforms under 2 m².</summary>
    public float MinRegionSize { get; init; } = 1.42f;
    public float MergeRegionSize { get; init; } = 10f;
    public float MaxEdgeLength { get; init; } = 12f;
    public float MaxEdgeError { get; init; } = 0.6f;

    /// <summary>The smallest stretch of open ground kept with no agent placed on it, in m².</summary>
    public float MinUnanchoredArea { get; init; } = 200f;

    /// <summary>The longest side a triangle keeps.</summary>
    public float MaxTriangleEdge { get; init; } = 12f;

    /// <summary>How far a triangle may stray from the ground or floor it lies on before it is split.</summary>
    public float MaxHeightError { get; init; } = 0.5f;
}

/// <summary>
/// Builds a navmesh sector by sector with Recast, from the terrain and the collision of what stands on
/// it, in the form the engine's own generator writes: links, graph ids, clearance radii, covers and the
/// quadtree. See docs/docs/file-formats/nvm.md.
/// </summary>
/// <remarks>
/// Each 64 m sector is one Recast tile, its corners put back onto the surfaces they stand for. The
/// tiles are welded where they meet and split where a neighbour's corner lands on an edge, ground
/// nothing reaches is dropped, and the rest is split to follow the surface and flipped into
/// well-shaped triangles. Every edge meets exactly one other or none, across sector seams too.
/// </remarks>
public static partial class NavMeshGenerator
{
    public const float SectorSize = 64f;

    /// <summary>The value world 1's sectors carry.</summary>
    public const ulong World1BuildId = 0x00051162F1B64C58;

    /// <summary>The clearance radii saturate here, as <c>ComputeSectorRadii</c>'s do.</summary>
    private const float MaxClearance = 10f;

    /// <summary><c>ComputeIsUnderWater</c>: a node this far under the surface is under water.</summary>
    private const float UnderWaterDepth = 0.6f;

    /// <summary>How far under the water surface retail lays every under-water node.</summary>
    private const float WaterFloorDepth = 0.62f;

    /// <summary>
    /// How close, in voxels, the tops of two faces in one column must be for a walkable one to make the
    /// other walkable too: one, so a slope too steep to walk never borrows from the face beside it.
    /// </summary>
    private const int FlagMerge = 1;

    /// <summary>A sector file's plan resolution: positions are packed to 1/256 m.</summary>
    private const float PlanSteps = 256f;

    /// <summary>
    /// How far a neighbouring sector's corner may sit off the height of a border edge it lies on and still split
    /// it: a long edge strays from the ground, and two floors are never closer than an agent is tall.
    /// </summary>
    private const float BorderStraying = 1f;

    /// <summary>The agent radius <c>SetGraphIdToNodeAndFriends</c> requires room for to flood into a node.</summary>
    private const float GraphRadius = 0.46f;

    /// <summary>Builds every sector of a map, reading its terrain, its placed entities, its trees and its
    /// world's entity library through <paramref name="readByPath"/>.</summary>
    /// <param name="knownPaths">Every path the trees' resource ids may name.</param>
    public static (Dictionary<int, NvmSector> Sectors, int Solids) BuildMap(
        TerrainMap map, string worldName, Func<string, byte[]?> readByPath, IEnumerable<string> knownPaths,
        FcbClassDefinitions definitions, IProgress<string>? progress = null)
    {
        WorldTerrain terrain = WorldTerrain.Load(map, readByPath, progress);
        Fc2World world = WorldLoader.Load(map, readByPath, progress);
        ArchetypeIndex archetypes = ArchetypeIndex.Load(worldName, readByPath, progress);
        List<NavMeshSolid> solids = NavMeshSolid.Of(world.Entities, archetypes, readByPath, progress);
        solids.AddRange(NavMeshSolid.OfVegetation(WorldVegetation.Load(map, readByPath, definitions, progress),
            WorldVegetation.ResourcesById(knownPaths), readByPath));
        Dictionary<int, float> water = terrain.Water.ToDictionary(w => w.SectorId, w => w.Level);

        progress?.Report($"Generating {map.Sectors.Count} sectors over {solids.Count} colliding objects");
        return (Build(map.SectorsPerSide, map.Sectors.Select(s => s.SectorId), NavMeshTerrain.Of(terrain),
            solids, AnchorsOf(world.Entities), id => water.TryGetValue(id, out float level) ? level : NvmSector.NoWater,
            progress: progress), solids.Count);
    }

    /// <summary>Where the AI stands or goes: soldiers, buddies, animals, vehicles and smart-terrain points.</summary>
    public static List<Vector3> AnchorsOf(IEnumerable<WorldEntity> entities)
        => [.. entities.Where(e => e.ArchetypeName.Split('.')[0] is "enemy_archetypes" or "buddies" or "Animals"
                or "vehicle" or "STP_archetypes" or "GhostPatrols")
            .Select(e => e.Position).OfType<Vector3>()];

    private readonly record struct Triangle(int Sector, Vector3 A, Vector3 B, Vector3 C, bool OnObject);

    /// <param name="anchors">Where agents are placed: a region off the ground is kept only when one stands on it.</param>
    /// <param name="waterLevelOf">A sector's water surface, or <see cref="NvmSector.NoWater"/>.</param>
    public static Dictionary<int, NvmSector> Build(
        int sectorsPerSide, IEnumerable<int> sectorIds, NavMeshTerrain terrain, IReadOnlyList<NavMeshSolid> solids,
        IReadOnlyList<Vector3> anchors, Func<int, float> waterLevelOf, NavMeshSettings? settings = null,
        ulong buildId = World1BuildId, IProgress<string>? progress = null)
    {
        settings ??= new NavMeshSettings();
        var clock = System.Diagnostics.Stopwatch.StartNew();

        // Under water the engine's mesh is a flat floor just deep enough to count as under water, which
        // boats path on; the bed below it is never walked.
        NavMeshTerrain ground = terrain with
        {
            Height = (x, y) =>
            {
                int column = Math.Clamp((int)MathF.Floor(x / SectorSize), 0, sectorsPerSide - 1);
                int row = Math.Clamp((int)MathF.Floor(y / SectorSize), 0, sectorsPerSide - 1);
                return MathF.Max(terrain.Height(x, y), waterLevelOf(row * sectorsPerSide + column) - WaterFloorDepth);
            },
        };
        var bySector = new Dictionary<(int, int), List<NavMeshSolid>>();
        foreach (NavMeshSolid solid in solids)
        {
            for (int y = (int)MathF.Floor((solid.Min.Y - 4) / SectorSize); y <= (int)MathF.Floor((solid.Max.Y + 4) / SectorSize); y++)
            {
                for (int x = (int)MathF.Floor((solid.Min.X - 4) / SectorSize); x <= (int)MathF.Floor((solid.Max.X + 4) / SectorSize); x++)
                {
                    (bySector.TryGetValue((x, y), out var list) ? list : bySector[(x, y)] = []).Add(solid);
                }
            }
        }

        var surface = new Surface(ground, solids, MathF.Cos(settings.MaxSlope * MathF.PI / 180));
        int[] ids = [.. sectorIds];
        var tiles = new List<Triangle>[ids.Length];
        // DotRecast writes its warnings straight to stdout, where they would break a JSON reply.
        TextWriter stdout = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            Parallel.For(0, ids.Length, i =>
            {
                int column = ids[i] % sectorsPerSide, row = ids[i] / sectorsPerSide;
                tiles[i] = Tile(ids[i], column * SectorSize, row * SectorSize, ground,
                    bySector.TryGetValue((column, row), out var near) ? near : [], surface, settings);
            });
        }
        finally
        {
            Console.SetOut(stdout);
        }

        progress?.Report($"Recast: {ids.Length} sectors, {tiles.Sum(t => t.Count):N0} triangles in {clock.Elapsed.TotalSeconds:F1} s");
        Mesh mesh = Reachable(Conform([.. tiles.SelectMany(t => t)], settings.MaxClimb), anchors, settings.MinUnanchoredArea);
        Refine(mesh, surface, settings.MaxTriangleEdge, settings.MaxHeightError);
        Flip(mesh, surface, settings.MaxHeightError);
        progress?.Report($"Joined {mesh.Triangles.Count:N0} triangles at {clock.Elapsed.TotalSeconds:F1} s");
        Dictionary<int, NvmSector> sectors = Assemble(sectorsPerSide, ids, mesh, waterLevelOf, solids, buildId);
        progress?.Report($"Assembled at {clock.Elapsed.TotalSeconds:F1} s");
        return sectors;
    }

    /// <summary>One sector's walkable surface as Recast builds it, in world space.</summary>
    private static List<Triangle> Tile(
        int id, float minX, float minY, NavMeshTerrain terrain, List<NavMeshSolid> solids, Surface surface, NavMeshSettings s)
    {
        const int border = 3;
        float pad = border * s.CellSize;
        float x0 = minX - pad, y0 = minY - pad, x1 = minX + SectorSize + pad, y1 = minY + SectorSize + pad;

        // Recast is Y up: a world point (x, y, z) is (x, z, y) there, which mirrors it, so every triangle
        // is handed over with its winding reversed to keep its upward face upward.
        var verts = new List<float>();
        var tris = new List<int>();
        var areas = new List<int>();
        float cosSlope = MathF.Cos(s.MaxSlope * MathF.PI / 180);
        float low = float.MaxValue, high = float.MinValue;
        void Add(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            float length = normal.Length();
            if (length < 1e-9f)
            {
                return;
            }
            int first = verts.Count / 3;
            foreach (Vector3 p in new[] { a, c, b })
            {
                verts.AddRange([p.X, p.Z, p.Y]);
                low = MathF.Min(low, p.Z);
                high = MathF.Max(high, p.Z);
            }
            tris.AddRange([first, first + 1, first + 2]);
            areas.Add(normal.Z / length >= cosSlope ? RcRecast.RC_WALKABLE_AREA : RcRecast.RC_NULL_AREA);
        }

        for (int y = (int)MathF.Floor(y0); y < (int)MathF.Ceiling(y1); y++)
        {
            for (int x = (int)MathF.Floor(x0); x < (int)MathF.Ceiling(x1); x++)
            {
                if (terrain.Hole(x, y))
                {
                    continue;
                }
                // The diagonal GetTerrainTesselation cuts every quad along.
                Vector3 p00 = new(x, y, terrain.Height(x, y)), p10 = new(x + 1, y, terrain.Height(x + 1, y));
                Vector3 p11 = new(x + 1, y + 1, terrain.Height(x + 1, y + 1)), p01 = new(x, y + 1, terrain.Height(x, y + 1));
                Add(p00, p10, p01);
                Add(p10, p11, p01);
            }
        }
        foreach (NavMeshSolid solid in solids)
        {
            if (solid.Max.X < x0 || solid.Min.X > x1 || solid.Max.Y < y0 || solid.Min.Y > y1)
            {
                continue;
            }
            foreach ((Vector3 a, Vector3 b, Vector3 c) in solid.Faces())
            {
                Add(a, b, c);
            }
        }
        if (areas.Count == 0)
        {
            return [];
        }

        var ctx = new RcContext();
        int cells = (int)MathF.Round(SectorSize / s.CellSize) + border * 2;
        int walkableHeight = (int)MathF.Ceiling(s.AgentHeight / s.CellHeight);
        int walkableClimb = (int)MathF.Floor(s.MaxClimb / s.CellHeight);
        var heightfield = new RcHeightfield(cells, cells, new RcVec3f(x0, low - 1, y0),
            new RcVec3f(x1, high + s.AgentHeight + 1, y1), s.CellSize, s.CellHeight, border);
        RcRasterizations.RasterizeTriangles(ctx, [.. verts], [.. tris], [.. areas], areas.Count, heightfield, FlagMerge);

        // The ground is solid below its surface, so nothing buried in it can be stood on.
        for (int cz = 0; cz < cells; cz++)
        {
            for (int cx = 0; cx < cells; cx++)
            {
                float wx = x0 + cx * s.CellSize, wy = y0 + cz * s.CellSize;
                if (terrain.Hole((int)MathF.Floor(wx + s.CellSize / 2), (int)MathF.Floor(wy + s.CellSize / 2)))
                {
                    continue;
                }
                float floor = MathF.Min(MathF.Min(SurfaceAt(terrain, wx, wy), SurfaceAt(terrain, wx + s.CellSize, wy)),
                    MathF.Min(SurfaceAt(terrain, wx, wy + s.CellSize), SurfaceAt(terrain, wx + s.CellSize, wy + s.CellSize)));
                int top = (int)MathF.Floor((floor - heightfield.bmin.Y) / s.CellHeight);
                if (top > 0)
                {
                    RcRasterizations.AddSpan(heightfield, cx, cz, 0, top, RcRecast.RC_NULL_AREA, FlagMerge);
                }
            }
        }
        RcFilters.FilterLowHangingWalkableObstacles(ctx, walkableClimb, heightfield);
        RcFilters.FilterLedgeSpans(ctx, walkableHeight, walkableClimb, heightfield);
        RcFilters.FilterWalkableLowHeightSpans(ctx, walkableHeight, heightfield);

        int Area(float size) => (int)MathF.Round(size / s.CellSize * (size / s.CellSize));
        RcContourSet Contours(RcCompactHeightfield compact) => RcContours.BuildContours(ctx, compact, s.MaxEdgeError,
            (int)(s.MaxEdgeLength / s.CellSize), RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);
        RcCompactHeightfield compact = RcCompacts.BuildCompactHeightfield(ctx, walkableHeight, walkableClimb, heightfield);
        RcContourSet contours;
        try
        {
            RcRegions.BuildDistanceField(ctx, compact);
            RcRegions.BuildRegions(ctx, compact, Area(s.MinRegionSize), Area(s.MergeRegionSize));
            contours = Contours(compact);
        }
        catch (Exception e) when (e.Message.Contains("Multiple outlines"))
        {
            // Watershed regions can wrap a hole with two outlines, which DotRecast refuses; monotone ones have no holes.
            compact = RcCompacts.BuildCompactHeightfield(ctx, walkableHeight, walkableClimb, heightfield);
            RcRegions.BuildRegionsMonotone(ctx, compact, Area(s.MinRegionSize), Area(s.MergeRegionSize));
            contours = Contours(compact);
        }
        RcPolyMesh polys = RcMeshs.BuildPolyMesh(ctx, contours, 6);
        // Recast cuts each polygon into well-shaped triangles. Allowed any error, it keeps none of its
        // samples, so the triangles' corners are the polygons' own, which neighbours share; Refine then
        // follows the ground.
        RcPolyMeshDetail detail = RcMeshDetails.BuildPolyMeshDetail(ctx, polys, compact, 1, float.MaxValue);

        // Recast's heights are its voxels' tops, up to a cell above the surface each stands for.
        Vector3[] snapped = [.. Enumerable.Range(0, detail.nverts).Select(v => surface.Snap(
            new Vector3(detail.verts[v * 3], detail.verts[v * 3 + 2], detail.verts[v * 3 + 1]), SnapDown, SnapUp))];
        var result = new List<Triangle>();
        for (int p = 0; p < detail.nmeshes; p++)
        {
            int vertBase = detail.meshes[p * 4], triBase = detail.meshes[p * 4 + 2], triCount = detail.meshes[p * 4 + 3];
            for (int t = triBase; t < triBase + triCount; t++)
            {
                Vector3 a = snapped[vertBase + detail.tris[t * 4]], b = snapped[vertBase + detail.tris[t * 4 + 1]];
                Vector3 c = snapped[vertBase + detail.tris[t * 4 + 2]];
                // Recast winds them for its mirrored axes; measured from a, since world coordinates are too
                // large to cross in single precision.
                if ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X) < 0)
                {
                    (b, c) = (c, b);
                }
                result.Add(new(id, a, b, c, surface.AboveTerrain(a, b, c)));
            }
        }
        return result;
    }

    /// <summary>How far below and above a Recast corner the surface it stands for may be.</summary>
    private const float SnapDown = 0.6f, SnapUp = 0.1f;

    /// <summary>
    /// What the mesh lies on: the ground, raised to the floor under any water, and every face of an object
    /// gentle enough to walk on, bucketed by plan position.
    /// </summary>
    private sealed class Surface
    {
        /// <summary>How far outside a face in plan a point still finds it: a Recast corner sits up to a cell off.</summary>
        private const float Reach = 0.3f;

        /// <summary>A point further than this from every surface in reach keeps its own height.</summary>
        private const float Storey = 3f;

        private const float AboveGround = 0.05f;

        private readonly NavMeshTerrain _ground;
        private readonly PlanBuckets<(Vector3 A, Vector3 B, Vector3 C)> _tops = new(2);

        public Surface(NavMeshTerrain ground, IEnumerable<NavMeshSolid> solids, float cosSlope)
        {
            _ground = ground;
            foreach (var face in solids.SelectMany(s => s.Faces()))
            {
                Vector3 normal = Vector3.Cross(face.B - face.A, face.C - face.A);
                if (normal.Z >= cosSlope * normal.Length())
                {
                    _tops.Add(Vector3.Min(face.A, Vector3.Min(face.B, face.C)) - new Vector3(Reach),
                        Vector3.Max(face.A, Vector3.Max(face.B, face.C)) + new Vector3(Reach), face);
                }
            }
        }

        /// <summary>
        /// <paramref name="p"/> on the nearest surface between <paramref name="down"/> below it and
        /// <paramref name="up"/> above; with none there, on the nearest of all when it is over a storey
        /// off, else where it is.
        /// </summary>
        public Vector3 Snap(Vector3 p, float down, float up)
        {
            float near = float.NaN, any = float.NaN;
            foreach (float surface in Heights(p.X, p.Y))
            {
                float rise = surface - p.Z;
                if (rise <= up && rise >= -down && (float.IsNaN(near) || MathF.Abs(rise) < MathF.Abs(near - p.Z)))
                {
                    near = surface;
                }
                if (float.IsNaN(any) || MathF.Abs(rise) < MathF.Abs(any - p.Z))
                {
                    any = surface;
                }
            }
            return p with { Z = !float.IsNaN(near) ? near : !float.IsNaN(any) && MathF.Abs(any - p.Z) > Storey ? any : p.Z };
        }

        /// <summary>The height of the surface nearest <paramref name="p"/> above or below it, if any is.</summary>
        public float? Nearest(Vector3 p)
        {
            float? best = null;
            foreach (float surface in Heights(p.X, p.Y))
            {
                if (best is not { } b || MathF.Abs(surface - p.Z) < MathF.Abs(b - p.Z))
                {
                    best = surface;
                }
            }
            return best;
        }

        /// <summary><c>ComputeIsAboveTerrain</c>: any corner over the ground, by more than a packed height step.</summary>
        public bool AboveTerrain(Vector3 a, Vector3 b, Vector3 c)
            => new[] { a, b, c }.Any(q => _ground.Hole((int)MathF.Floor(q.X), (int)MathF.Floor(q.Y))
                || q.Z > SurfaceAt(_ground, q.X, q.Y) + AboveGround);

        private IEnumerable<float> Heights(float x, float y)
        {
            if (!_ground.Hole((int)MathF.Floor(x), (int)MathF.Floor(y)))
            {
                yield return SurfaceAt(_ground, x, y);
            }
            var p = new Vector3(x, y, 0);
            foreach ((Vector3 a, Vector3 b, Vector3 c) in _tops.In(p, p))
            {
                if (Height(a, b, c, p, Reach) is float z)
                {
                    yield return z;
                }
            }
        }
    }

    /// <summary>Things filed under every plan cell their box covers, so a point or a box finds those near it.</summary>
    private sealed class PlanBuckets<T>(float cell)
    {
        private readonly Dictionary<(int, int), List<T>> _cells = [];

        public void Add(Vector3 min, Vector3 max, T item)
        {
            for (int y = Key(min.Y); y <= Key(max.Y); y++)
            {
                for (int x = Key(min.X); x <= Key(max.X); x++)
                {
                    (_cells.TryGetValue((x, y), out var list) ? list : _cells[(x, y)] = []).Add(item);
                }
            }
        }

        /// <summary>What is filed in the cells the box covers, an item once for each of them it is in.</summary>
        public IEnumerable<T> In(Vector3 min, Vector3 max)
        {
            for (int y = Key(min.Y); y <= Key(max.Y); y++)
            {
                for (int x = Key(min.X); x <= Key(max.X); x++)
                {
                    if (_cells.TryGetValue((x, y), out var list))
                    {
                        foreach (T item in list)
                        {
                            yield return item;
                        }
                    }
                }
            }
        }

        private int Key(float v) => (int)MathF.Floor(v / cell);
    }

    /// <summary>The terrain's surface at a world position, on the triangles the quads are cut into.</summary>
    private static float SurfaceAt(NavMeshTerrain terrain, float x, float y)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        return fx + fy <= 1
            ? terrain.Height(ix, iy) + (terrain.Height(ix + 1, iy) - terrain.Height(ix, iy)) * fx + (terrain.Height(ix, iy + 1) - terrain.Height(ix, iy)) * fy
            : terrain.Height(ix + 1, iy + 1) + (terrain.Height(ix, iy + 1) - terrain.Height(ix + 1, iy + 1)) * (1 - fx)
              + (terrain.Height(ix + 1, iy) - terrain.Height(ix + 1, iy + 1)) * (1 - fy);
    }

    /// <summary>The tiles' triangles as one indexed mesh, welded and with no corner left on another's edge.</summary>
    private sealed class Mesh
    {
        public readonly List<Vector3> Vertices = [];
        public readonly List<(int Sector, int A, int B, int C, bool OnObject)> Triangles = [];

        public int Corner(int t, int k) => k switch { 0 => Triangles[t].A, 1 => Triangles[t].B, _ => Triangles[t].C };

        public (Vector3 Min, Vector3 Max) Bounds(int t)
        {
            Vector3 a = Vertices[Triangles[t].A], b = Vertices[Triangles[t].B], c = Vertices[Triangles[t].C];
            return (Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
        }

        public static (int, int) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

        /// <summary>The triangles on each edge, either way round.</summary>
        public Dictionary<(int, int), List<int>> Edges()
        {
            var edges = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t < Triangles.Count; t++)
            {
                File(edges, t);
            }
            return edges;
        }

        public void File(Dictionary<(int, int), List<int>> edges, int t)
        {
            for (int k = 0; k < 3; k++)
            {
                var key = EdgeKey(Corner(t, k), Corner(t, (k + 1) % 3));
                (edges.TryGetValue(key, out var list) ? list : edges[key] = []).Add(t);
            }
        }
    }

    /// <param name="climb">Corners on one grid point closer than this in height are one: what an agent steps across is joined anyway.</param>
    private static Mesh Conform(List<Triangle> triangles, float climb)
    {
        // Corners are put on the grid a sector file packs them to, so what is joined here stays joined in the file.
        const float minArea = 1e-4f;
        var mesh = new Mesh();
        var buckets = new Dictionary<(long, long), List<int>>();
        int Vertex(Vector3 p)
        {
            var key = ((long)MathF.Round(p.X * PlanSteps), (long)MathF.Round(p.Y * PlanSteps));
            if (!buckets.TryGetValue(key, out var list))
            {
                buckets[key] = list = [];
            }
            float z = MathF.Round(p.Z / NvmSectorFile.HeightStep) * NvmSectorFile.HeightStep;
            foreach (int i in list)
            {
                if (MathF.Abs(mesh.Vertices[i].Z - z) <= climb)
                {
                    return i;
                }
            }
            list.Add(mesh.Vertices.Count);
            mesh.Vertices.Add(new(key.Item1 / PlanSteps, key.Item2 / PlanSteps, z));
            return mesh.Vertices.Count - 1;
        }
        var welded = new List<(int Sector, int A, int B, int C, bool OnObject)>();
        foreach (Triangle t in triangles)
        {
            int a = Vertex(t.A), b = Vertex(t.B), c = Vertex(t.C);
            Vector3 pa = mesh.Vertices[a], pb = mesh.Vertices[b], pc = mesh.Vertices[c];
            if ((pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X) > minArea * 2)
            {
                welded.Add((t.Sector, a, b, c, t.OnObject));
            }
        }

        // Two sectors' corners along the border between them differ, so a corner of one lying on an edge of
        // the other splits that edge, and both sides meet corner to corner.
        var onBorder = new Dictionary<(bool, float), List<int>>();
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            Vector3 p = mesh.Vertices[i];
            if (p.X % SectorSize == 0)
            {
                (onBorder.TryGetValue((true, p.X), out var list) ? list : onBorder[(true, p.X)] = []).Add(i);
            }
            if (p.Y % SectorSize == 0)
            {
                (onBorder.TryGetValue((false, p.Y), out var list) ? list : onBorder[(false, p.Y)] = []).Add(i);
            }
        }
        List<int> OnSegment(int a, int b)
        {
            Vector3 pa = mesh.Vertices[a], pb = mesh.Vertices[b];
            bool alongY = pa.X == pb.X && pa.X % SectorSize == 0;
            if (!alongY && !(pa.Y == pb.Y && pa.Y % SectorSize == 0)
                || !onBorder.TryGetValue((alongY, alongY ? pa.X : pa.Y), out var candidates))
            {
                return [];
            }
            float from = alongY ? pa.Y : pa.X, to = alongY ? pb.Y : pb.X;
            var found = new List<(float T, int V)>();
            foreach (int v in candidates)
            {
                Vector3 p = mesh.Vertices[v];
                float t = ((alongY ? p.Y : p.X) - from) / (to - from);
                if (t > 0 && t < 1 && MathF.Abs(p.Z - (pa.Z + (pb.Z - pa.Z) * t)) <= BorderStraying)
                {
                    found.Add((t, v));
                }
            }
            return [.. found.OrderBy(f => f.T).Select(f => f.V)];
        }

        foreach (var (sector, a, b, c, onObject) in welded)
        {
            List<int>[] sides = [OnSegment(a, b), OnSegment(b, c), OnSegment(c, a)];
            int split = sides.Count(s => s.Count > 0);
            if (split == 0)
            {
                mesh.Triangles.Add((sector, a, b, c, onObject));
                continue;
            }
            int[] corners = [a, b, c];
            if (split == 1)
            {
                // Fanned from the corner facing the split side, no piece is degenerate.
                int k = Array.FindIndex(sides, s => s.Count > 0);
                int from = corners[k], to = corners[(k + 1) % 3], apex = corners[(k + 2) % 3];
                int previous = from;
                foreach (int v in sides[k].Append(to))
                {
                    mesh.Triangles.Add((sector, apex, previous, v, onObject));
                    previous = v;
                }
                continue;
            }
            List<int> ring = [];
            for (int k = 0; k < 3; k++)
            {
                ring.Add(corners[k]);
                ring.AddRange(sides[k]);
            }
            mesh.Vertices.Add((mesh.Vertices[a] + mesh.Vertices[b] + mesh.Vertices[c]) / 3);
            int centre = mesh.Vertices.Count - 1;
            for (int k = 0; k < ring.Count; k++)
            {
                mesh.Triangles.Add((sector, centre, ring[k], ring[(k + 1) % ring.Count], onObject));
            }
        }
        return mesh;
    }

    /// <summary>
    /// Splits every triangle longer than <paramref name="longest"/>, or further than <paramref name="tolerance"/>
    /// off the surface somewhere it is sampled, at the middle of its longest side, the new corner put on the
    /// surface. Whatever else shares that side is split there too, so the edges still meet corner to corner.
    /// </summary>
    private static void Refine(Mesh mesh, Surface surface, float longest, float tolerance)
    {
        // The terrain is sampled a metre apart, so nothing finer than this follows it any better.
        const float shortest = 1.5f, sliver = 0.05f;
        var tris = mesh.Triangles;
        Dictionary<(int, int), List<int>> edges = mesh.Edges();
        static (int, int) Key(int a, int b) => Mesh.EdgeKey(a, b);

        bool Strays(int t)
        {
            Vector3 a = mesh.Vertices[tris[t].A], b = mesh.Vertices[tris[t].B], c = mesh.Vertices[tris[t].C];
            foreach (Vector3 p in new[] { (a + b + c) / 3, (a + b) / 2, (b + c) / 2, (c + a) / 2 })
            {
                if (surface.Nearest(p) is float z && MathF.Abs(z - p.Z) > tolerance)
                {
                    return true;
                }
            }
            return false;
        }

        // A bound on the splitting, so a surface no tolerance settles on cannot run away with the memory:
        // a few times what the mesh holds, and what its area needs to get under the longest side.
        double area = Enumerable.Range(0, tris.Count).Sum(t => (double)PlanArea(t));
        int budget = (int)Math.Min(int.MaxValue / 2, 4 * (tris.Count + area / (longest * longest / 4)));
        var queue = new Queue<int>(Enumerable.Range(0, tris.Count));
        while (budget > 0 && queue.TryDequeue(out int t))
        {
            int[] corners = [tris[t].A, tris[t].B, tris[t].C];
            int k = Enumerable.Range(0, 3).MaxBy(i => PlanLength(mesh.Vertices[corners[i]], mesh.Vertices[corners[(i + 1) % 3]]));
            float length = PlanLength(mesh.Vertices[corners[k]], mesh.Vertices[corners[(k + 1) % 3]]);
            // A sliver along a line never shortens by halving, so it only splits when its neighbour does.
            if (length <= shortest || PlanArea(t) < sliver || (length <= longest && !Strays(t)))
            {
                continue;
            }

            int a = corners[k], b = corners[(k + 1) % 3];
            Vector3 middle = (mesh.Vertices[a] + mesh.Vertices[b]) / 2;
            mesh.Vertices.Add(surface.Nearest(middle) is float z ? middle with { Z = z } : middle);
            int m = mesh.Vertices.Count - 1;
            foreach (int u in edges[Key(a, b)].ToList())
            {
                var (sector, p, q, r) = tris[u].A == a || tris[u].A == b
                    ? tris[u].B == a || tris[u].B == b ? (tris[u].Sector, tris[u].A, tris[u].B, tris[u].C) : (tris[u].Sector, tris[u].C, tris[u].A, tris[u].B)
                    : (tris[u].Sector, tris[u].B, tris[u].C, tris[u].A);
                edges[Key(q, r)].Remove(u);
                tris[u] = (sector, p, m, r, surface.AboveTerrain(mesh.Vertices[p], mesh.Vertices[m], mesh.Vertices[r]));
                tris.Add((sector, m, q, r, surface.AboveTerrain(mesh.Vertices[m], mesh.Vertices[q], mesh.Vertices[r])));
                int w = tris.Count - 1;
                budget--;
                (edges.TryGetValue(Key(p, m), out var pm) ? pm : edges[Key(p, m)] = []).Add(u);
                (edges.TryGetValue(Key(m, r), out var mr) ? mr : edges[Key(m, r)] = []).Add(u);
                mesh.File(edges, w);
                queue.Enqueue(u);
                queue.Enqueue(w);
            }
            edges.Remove(Key(a, b));
        }

        static float PlanLength(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

        float PlanArea(int t)
        {
            Vector3 a = mesh.Vertices[tris[t].A], b = mesh.Vertices[tris[t].B], c = mesh.Vertices[tris[t].C];
            return MathF.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
        }
    }

    /// <summary>
    /// Flips the diagonal of every pair of triangles that would be better shaped cut the other way (the
    /// Delaunay test, in plan) and still follow the surface within <paramref name="tolerance"/>, so
    /// Recast's slivers become fair triangles. Pairs from two sectors keep their edge, which is the
    /// border between them.
    /// </summary>
    private static void Flip(Mesh mesh, Surface surface, float tolerance)
    {
        var tris = mesh.Triangles;
        Dictionary<(int, int), List<int>> edges = mesh.Edges();
        static (int, int) Key(int a, int b) => Mesh.EdgeKey(a, b);

        // The corner of t opposite the edge a-b, where t runs a to b.
        int Opposite(int t, int a, int b)
        {
            for (int k = 0; k < 3; k++)
            {
                if (mesh.Corner(t, k) == a && mesh.Corner(t, (k + 1) % 3) == b)
                {
                    return mesh.Corner(t, (k + 2) % 3);
                }
            }
            return -1;
        }

        var queue = new Queue<(int, int)>(edges.Keys);
        int budget = tris.Count * 10;
        while (budget-- > 0 && queue.TryDequeue(out var edge))
        {
            if (!edges.TryGetValue(edge, out var pair) || pair.Count != 2 || tris[pair[0]].Sector != tris[pair[1]].Sector)
            {
                continue;
            }
            // t1 runs a to b, t2 b to a: c is left of a-b, d right of it.
            var (a, b) = edge;
            int t1 = pair[0], t2 = pair[1];
            if (Opposite(t1, a, b) < 0)
            {
                (a, b) = (b, a);
            }
            int c = Opposite(t1, a, b), d = Opposite(t2, b, a);
            if (c < 0 || d < 0 || c == d)
            {
                continue;
            }
            Vector3 pa = mesh.Vertices[a], pb = mesh.Vertices[b], pc = mesh.Vertices[c], pd = mesh.Vertices[d];
            // The quad must be convex, d inside a-b-c's circle, and the new pair on the surface.
            if (Cross(pc, pd, pa) >= 0 || Cross(pc, pd, pb) <= 0 || !InCircle(pa, pb, pc, pd)
                || new[] { (pc + pd) / 2, (pa + pd + pc) / 3, (pd + pb + pc) / 3 }.Any(p => surface.Nearest(p) is float z && MathF.Abs(z - p.Z) > tolerance))
            {
                continue;
            }
            int sector = tris[t1].Sector;
            tris[t1] = (sector, a, d, c, surface.AboveTerrain(pa, pd, pc));
            tris[t2] = (sector, d, b, c, surface.AboveTerrain(pd, pb, pc));
            edges.Remove(Key(a, b));
            edges[Key(c, d)] = [t1, t2];
            edges[Key(a, d)][edges[Key(a, d)].IndexOf(t2)] = t1;
            edges[Key(b, c)][edges[Key(b, c)].IndexOf(t1)] = t2;
            foreach (var next in new[] { Key(a, d), Key(d, b), Key(b, c), Key(c, a) })
            {
                queue.Enqueue(next);
            }
        }

        // Positive when r is left of p-q, measured from p.
        static float Cross(Vector3 p, Vector3 q, Vector3 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);

        // Whether d is inside the circle through the counter-clockwise a, b, c, measured from d.
        static bool InCircle(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            double ax = a.X - d.X, ay = a.Y - d.Y, bx = b.X - d.X, by = b.Y - d.Y, cx = c.X - d.X, cy = c.Y - d.Y;
            double det = (ax * ax + ay * ay) * (bx * cy - cx * by) - (bx * bx + by * by) * (ax * cy - cx * ay)
                + (cx * cx + cy * cy) * (ax * by - bx * ay);
            return det > 1e-9;
        }
    }

    /// <summary>The triangle across each edge, or -1: only where the two name each other, so every link has its way back.</summary>
    private static int[] Neighbours(Mesh mesh)
    {
        var owner = new Dictionary<(int, int), int>();
        for (int t = 0; t < mesh.Triangles.Count; t++)
        {
            var (_, a, b, c, _) = mesh.Triangles[t];
            owner.TryAdd((a, b), t);
            owner.TryAdd((b, c), t);
            owner.TryAdd((c, a), t);
        }
        int Across(int t, int k) => owner.TryGetValue((mesh.Corner(t, (k + 1) % 3), mesh.Corner(t, k)), out int other) && other != t ? other : -1;
        var neighbours = new int[mesh.Triangles.Count * 3];
        for (int i = 0; i < neighbours.Length; i++)
        {
            int other = Across(i / 3, i % 3);
            neighbours[i] = other >= 0 && Enumerable.Range(0, 3).Any(k => Across(other, k) == i / 3) ? other : -1;
        }
        return neighbours;
    }

    /// <summary>
    /// The mesh without what no agent reaches. The engine's <c>FilterUnusedAreas</c> keeps only the regions
    /// a placed AI agent stands in; this keeps those and every region of open ground at least
    /// <paramref name="minArea"/> across, so rooftops, the tops of rocks and scraps of mountainside go
    /// unless something is placed on them, while ground an agent may yet be spawned on stays.
    /// </summary>
    private static Mesh Reachable(Mesh mesh, IReadOnlyList<Vector3> anchors, float minArea)
    {
        int[] neighbours = Neighbours(mesh);
        var region = new int[mesh.Triangles.Count];
        Array.Fill(region, -1);
        var keep = new List<bool>();
        var stack = new Stack<int>();
        for (int start = 0; start < region.Length; start++)
        {
            if (region[start] >= 0)
            {
                continue;
            }
            bool grounded = false;
            float area = 0;
            region[start] = keep.Count;
            stack.Push(start);
            while (stack.TryPop(out int t))
            {
                grounded |= !mesh.Triangles[t].OnObject;
                Vector3 a = mesh.Vertices[mesh.Corner(t, 0)], b = mesh.Vertices[mesh.Corner(t, 1)], c = mesh.Vertices[mesh.Corner(t, 2)];
                area += ((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X)) / 2;
                for (int k = 0; k < 3; k++)
                {
                    if (neighbours[t * 3 + k] is int next and >= 0 && region[next] < 0)
                    {
                        region[next] = keep.Count;
                        stack.Push(next);
                    }
                }
            }
            keep.Add(grounded && area >= minArea);
        }

        // An anchor holds the region whose surface it stands on, or just over.
        const float standAbove = 2f, standBelow = 0.5f;
        var lofty = new PlanBuckets<int>(4);
        for (int t = 0; t < region.Length; t++)
        {
            if (!keep[region[t]])
            {
                (Vector3 min, Vector3 max) = mesh.Bounds(t);
                lofty.Add(min, max, t);
            }
        }
        foreach (Vector3 anchor in anchors)
        {
            foreach (int t in lofty.In(anchor, anchor))
            {
                if (Height(mesh.Vertices[mesh.Corner(t, 0)], mesh.Vertices[mesh.Corner(t, 1)], mesh.Vertices[mesh.Corner(t, 2)], anchor) is float z
                    && anchor.Z - z is >= -standBelow and <= standAbove)
                {
                    keep[region[t]] = true;
                }
            }
        }

        var kept = new Mesh();
        kept.Vertices.AddRange(mesh.Vertices);
        kept.Triangles.AddRange(mesh.Triangles.Where((_, t) => keep[region[t]]));
        return kept;
    }

    /// <summary>The triangle's height under a point, or null when the point is outside it in plan.</summary>
    /// <param name="margin">How far outside the triangle in plan a point may be, its plane carried out to it.</param>
    private static float? Height(Vector3 a, Vector3 b, Vector3 c, Vector3 p, float margin = 0)
    {
        float d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (MathF.Abs(d) < 1e-9f)
        {
            return null;
        }
        float u = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / d;
        float v = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / d;
        bool inside = u >= -1e-3f && v >= -1e-3f && 1 - u - v >= -1e-3f;
        return inside || (margin > 0 && MathF.Min(PlanDistance(p, a, b), MathF.Min(PlanDistance(p, b, c), PlanDistance(p, c, a))) <= margin)
            ? u * a.Z + v * b.Z + (1 - u - v) * c.Z
            : null;

        static float PlanDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = new Vector2(b.X - a.X, b.Y - a.Y);
            var ap = new Vector2(p.X - a.X, p.Y - a.Y);
            float t = Math.Clamp(Vector2.Dot(ap, ab) / MathF.Max(ab.LengthSquared(), 1e-12f), 0, 1);
            return Vector2.Distance(ap, ab * t);
        }
    }

    /// <summary>Packs each sector's triangles and derives what the engine reads alongside them.</summary>
    private static Dictionary<int, NvmSector> Assemble(
        int sectorsPerSide, int[] sectorIds, Mesh mesh, Func<int, float> waterLevelOf, IReadOnlyList<NavMeshSolid> solids, ulong buildId)
    {
        var bySector = sectorIds.ToDictionary(id => id, _ => new List<int>());
        for (int t = 0; t < mesh.Triangles.Count; t++)
        {
            bySector[mesh.Triangles[t].Sector].Add(t);
        }
        // Each triangle's place in its sector's node array, as a link names it.
        var nodeOf = new uint[mesh.Triangles.Count];
        foreach ((int id, List<int> list) in bySector)
        {
            if (list.Count >= ushort.MaxValue)
            {
                throw new InvalidOperationException($"Sector {id} needs {list.Count} triangles; a link holds 65535.");
            }
            for (int n = 0; n < list.Count; n++)
            {
                nodeOf[list[n]] = (uint)n << 16 | (uint)id;
            }
        }

        int[] neighbours = Neighbours(mesh);
        int Corner(int t, int k) => mesh.Corner(t, k);
        var water = new bool[mesh.Triangles.Count];
        for (int t = 0; t < mesh.Triangles.Count; t++)
        {
            float level = waterLevelOf(mesh.Triangles[t].Sector) - UnderWaterDepth + 0.001f;
            water[t] = Enumerable.Range(0, 3).All(k => mesh.Vertices[Corner(t, k)].Z <= level);
        }

        // What blocks an agent, as FindMaximumFittingRadius walks out to it: an edge with nothing across, or
        // with a node across it may not enter, which between dry ground and water is either way.
        var boundary = new BoundaryIndex(Enumerable.Range(0, neighbours.Length)
            .Where(i => neighbours[i] < 0 || water[neighbours[i]] != water[i / 3])
            .Select(i => (mesh.Vertices[Corner(i / 3, i % 3)], mesh.Vertices[Corner(i / 3, (i % 3 + 1) % 3)])));
        var radii = new NvmClearance[mesh.Triangles.Count];
        Parallel.For(0, radii.Length, t =>
        {
            Vector3 a = mesh.Vertices[Corner(t, 0)], b = mesh.Vertices[Corner(t, 1)], c = mesh.Vertices[Corner(t, 2)];
            Vector3[] samples = NvmNode.SamplePoints((a + b + c) / 3, a, b, c);
            for (int k = 0; k < samples.Length; k++)
            {
                radii[t][k] = (byte)(int)(MathF.Min(boundary.DistanceTo(samples[k]), MaxClearance) * 25);
            }
        });
        ushort[] graphIds = GraphIds(neighbours, water, radii);
        var covers = Covers(mesh, neighbours, graphIds, boundary, solids);

        var sectors = new Dictionary<int, NvmSector>();
        foreach ((int id, List<int> list) in bySector)
        {
            float minX = id % sectorsPerSide * SectorSize, minY = id / sectorsPerSide * SectorSize;
            var shell = new NvmSector
            {
                SectorWord = (ushort)(id % sectorsPerSide << 7 | id / sectorsPerSide),
                SectorId = (ushort)id,
                MinX = minX,
                MinY = minY,
                MaxX = minX + SectorSize,
                MaxY = minY + SectorSize,
                WaterLevel = waterLevelOf(id),
                BuildId = buildId,
                Unknown30 = 0x7F7F,
                Links = [],
                Nodes = [],
                Vertices = [],
                Covers = [],
                DynamicCovers = [],
                CoverVertices = [],
                Quads = [],
                QuadNodes = [],
            };

            var index = new Dictionary<int, uint>();
            var packed = new List<PackedVector>();
            uint IndexOf(int v)
            {
                if (!index.TryGetValue(v, out uint i))
                {
                    index[v] = i = (uint)packed.Count;
                    packed.Add(shell.Pack(mesh.Vertices[v]));
                }
                return i;
            }

            var nodes = new NvmNode[list.Count];
            var sectorLinks = new uint[list.Count * 3];
            var flat = new (Vector2, Vector2, Vector2)[list.Count];
            for (int n = 0; n < list.Count; n++)
            {
                int t = list[n];
                var (_, ia, ib, ic, onObject) = mesh.Triangles[t];
                Vector3 a = mesh.Vertices[ia], b = mesh.Vertices[ib], c = mesh.Vertices[ic];
                Vector3 centroid = (a + b + c) / 3;
                Vector3 cross = Vector3.Cross(b - a, c - a);
                Vector3 normal = cross.LengthSquared() > 1e-12f ? Vector3.Normalize(cross) : Vector3.UnitZ;
                for (int k = 0; k < 3; k++)
                {
                    sectorLinks[n * 3 + k] = neighbours[t * 3 + k] is int other and >= 0 ? nodeOf[other] : NvmSector.NoLink;
                }
                nodes[n] = new NvmNode(graphIds[t], 3, (uint)n * 3, shell.Pack(centroid),
                    Normal(normal.X), Normal(normal.Y), Normal(normal.Z), water[t], onObject,
                    IndexOf(ia), IndexOf(ib), IndexOf(ic), radii[t]);
                flat[n] = (new(a.X, a.Y), new(b.X, b.Y), new(c.X, c.Y));
            }

            (NvmQuad[] quads, ushort[] quadNodes) = NvmSectorFile.BuildQuadTree(shell.MinX, shell.MinY, shell.MaxX, shell.MaxY, flat);
            if (quadNodes.Length > ushort.MaxValue)
            {
                throw new InvalidOperationException($"Sector {id}'s quadtree lists {quadNodes.Length} entries; a quad's start holds 65535.");
            }
            sectors[id] = shell with
            {
                Links = sectorLinks,
                Nodes = nodes,
                Vertices = [.. packed],
                Quads = quads,
                QuadNodes = quadNodes,
                Covers = covers.TryGetValue(id, out var sectorCovers) ? [.. sectorCovers.Covers] : [],
                CoverVertices = sectorCovers.Vertices is { } coverVertices ? [.. coverVertices.Select(shell.Pack)] : [],
            };
        }
        return sectors;

        static sbyte Normal(float component) => (sbyte)MathF.Round(component * 127);
    }

    /// <summary>Numbers the linked regions from 1, largest first, as <c>SetGraphIdToNodeAndFriends</c>
    /// floods them: across links between triangles alike in being under water or not, where
    /// an agent of <see cref="GraphRadius"/> fits through the shared edge on the far side.</summary>
    private static ushort[] GraphIds(int[] neighbours, bool[] water, NvmClearance[] radii)
    {
        // IdentifyPositionIndexes: the slots of a node's edge k are its two corners and its middle.
        int[] middle = [4, 6, 5];
        bool Fits(int node, int edge)
            => new[] { 1 + edge, 1 + (edge + 1) % 3, middle[edge] }.Any(slot => radii[node][slot] / 25f > GraphRadius);
        bool Enters(int from, int next)
        {
            int edge = Array.FindIndex([neighbours[next * 3], neighbours[next * 3 + 1], neighbours[next * 3 + 2]], n => n == from);
            return edge >= 0 && Fits(next, edge);
        }

        int count = water.Length;
        var region = new int[count];
        Array.Fill(region, -1);
        var sizes = new List<int>();
        var stack = new Stack<int>();
        for (int start = 0; start < count; start++)
        {
            if (region[start] >= 0)
            {
                continue;
            }
            int id = sizes.Count, size = 0;
            region[start] = id;
            stack.Push(start);
            while (stack.TryPop(out int t))
            {
                size++;
                for (int k = 0; k < 3; k++)
                {
                    if (neighbours[t * 3 + k] is int next and >= 0 && region[next] < 0 && water[next] == water[t] && Enters(t, next))
                    {
                        region[next] = id;
                        stack.Push(next);
                    }
                }
            }
            sizes.Add(size);
        }
        int[] rank = new int[sizes.Count];
        int r = 1;
        foreach (int id in Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i]))
        {
            rank[id] = Math.Min(r++, ushort.MaxValue - 1);
        }
        return [.. region.Select(id => (ushort)rank[id])];
    }

    /// <summary>The navmesh's open edges, bucketed so a point finds those within reach.</summary>
    private sealed class BoundaryIndex
    {
        private readonly PlanBuckets<(Vector3 A, Vector3 B)> _edges = new(MaxClearance);

        public BoundaryIndex(IEnumerable<(Vector3 A, Vector3 B)> edges)
        {
            foreach ((Vector3 a, Vector3 b) in edges)
            {
                _edges.Add(Vector3.Min(a, b), Vector3.Max(a, b), (a, b));
            }
        }

        public float DistanceTo(Vector3 p)
        {
            float best = MaxClearance;
            foreach ((Vector3 a, Vector3 b) in _edges.In(p - new Vector3(MaxClearance), p + new Vector3(MaxClearance)))
            {
                Vector3 ab = b - a;
                float t = Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
                best = MathF.Min(best, Vector3.Distance(p, a + ab * t));
            }
            return best;
        }
    }
}
