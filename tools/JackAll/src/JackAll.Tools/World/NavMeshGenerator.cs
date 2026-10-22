using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>Ground an agent cannot stand on: a convex footprint, counter-clockwise.</summary>
public sealed record NavMeshObstacle(Vector2[] Footprint)
{
    public bool Contains(Vector2 p)
    {
        for (int i = 0; i < Footprint.Length; i++)
        {
            Vector2 a = Footprint[i], b = Footprint[(i + 1) % Footprint.Length];
            if ((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X) < 0)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Whether the footprint and the axis-aligned square share any area (separating axes).</summary>
    public bool Overlaps(float x, float y, float size)
    {
        Vector2[] square = [new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)];
        if (Footprint.All(p => p.X >= x + size) || Footprint.All(p => p.X <= x)
            || Footprint.All(p => p.Y >= y + size) || Footprint.All(p => p.Y <= y))
        {
            return false;
        }
        for (int i = 0; i < Footprint.Length; i++)
        {
            Vector2 a = Footprint[i], b = Footprint[(i + 1) % Footprint.Length];
            var outward = new Vector2(b.Y - a.Y, a.X - b.X);
            float limit = Vector2.Dot(outward, a);
            if (square.All(p => Vector2.Dot(outward, p) >= limit))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The footprints of placed entities that collide: anything whose descriptor carries a static
    /// physics component and stands taller than an agent steps over, its model's box grown by a margin
    /// that keeps a body off it.
    /// </summary>
    public static List<NavMeshObstacle> Of(IEnumerable<WorldEntity> entities, ArchetypeIndex archetypes)
    {
        const float minHeight = 0.4f, margin = 0.25f;
        var obstacles = new List<NavMeshObstacle>();
        foreach (WorldEntity entity in entities)
        {
            XElement? descriptor = FcbEntityFields.ReadDescriptor(entity.Node)
                ?? (archetypes.Winner(entity.ArchetypeName) is { } archetype
                    ? FcbEntityFields.ReadDescriptor(archetype.Node)
                    : null);
            if (entity.Position is not { } position || descriptor is null
                || !descriptor.Descendants("component").Any(c =>
                    (string?)c.Attribute("class") is "StaticPhysComponent" or "CompoundPhysComponent")
                || ModelBox(descriptor) is not (Vector3 min, Vector3 max) || max.Z - min.Z < minHeight)
            {
                continue;
            }

            Matrix4x4 rotation = entity.Rotation;
            obstacles.Add(new NavMeshObstacle([.. new[]
            {
                new Vector3(min.X - margin, min.Y - margin, 0), new Vector3(max.X + margin, min.Y - margin, 0),
                new Vector3(max.X + margin, max.Y + margin, 0), new Vector3(min.X - margin, max.Y + margin, 0),
            }.Select(corner =>
            {
                Vector3 placed = Vector3.Transform(corner, rotation) + position;
                return new Vector2(placed.X, placed.Y);
            })]));
        }
        return obstacles;
    }

    /// <summary>The whole model's box, from the graphic component's <c>.xbg</c> resource.</summary>
    private static (Vector3 Min, Vector3 Max)? ModelBox(XElement descriptor)
    {
        XElement? resource = descriptor.Descendants("component")
            .Where(c => (string?)c.Attribute("class") == "GraphicComponent")
            .Elements("resource")
            .FirstOrDefault(r => ((string?)r.Attribute("fileName"))?.EndsWith(".xbg", StringComparison.OrdinalIgnoreCase) == true);
        return resource is not null && Parse(resource.Attribute("bboxMin")) is { } min
               && Parse(resource.Attribute("bboxMax")) is { } max
            ? (min, max)
            : null;

        static Vector3? Parse(XAttribute? attribute)
        {
            string[] parts = ((string?)attribute ?? "").Split(',');
            return parts.Length == 3
                   && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                   && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                   && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                ? new Vector3(x, y, z)
                : null;
        }
    }
}

/// <summary>
/// Builds a navmesh for ground with obstacles standing on it, sector by sector, in the form the
/// engine's own generator writes: links, graph ids, clearance radii and the quadtree included.
/// </summary>
/// <remarks>
/// Each 64 m sector is a quadtree of square cells, split where an obstacle's edge or the terrain's
/// bend passes through, then balanced so neighbours differ by at most one level. A cell becomes a
/// fan from its centre through its corners and the midpoints of any side a finer neighbour shares,
/// so every edge meets exactly one other and no triangle has a T-junction. Steep slopes are not cut
/// out, and no cover points are made.
/// </remarks>
public static class NavMeshGenerator
{
    public const float SectorSize = 64f;

    /// <summary>The value world 1's sectors carry.</summary>
    public const ulong World1BuildId = 0x00051162F1B64C58;

    private const float MinCell = 0.5f;
    private const float MaxCell = 16f;

    /// <summary>How far the terrain may stray from a cell's flat triangles before the cell splits.</summary>
    private const float MaxHeightError = 0.25f;

    /// <summary>The clearance radii saturate here, as <c>ComputeSectorRadii</c>'s do.</summary>
    private const float MaxClearance = 10f;

    /// <summary><c>ComputeIsUnderWater</c>: a node this far under the surface is under water.</summary>
    private const float UnderWaterDepth = 0.6f;

    /// <summary>Builds every sector of a map, reading its terrain, its placed entities and its
    /// world's entity library through <paramref name="readByPath"/>.</summary>
    public static (Dictionary<int, NvmSector> Sectors, int Obstacles) BuildMap(
        TerrainMap map, string worldName, Func<string, byte[]?> readByPath, IProgress<string>? progress = null)
    {
        WorldTerrain terrain = WorldTerrain.Load(map, readByPath, progress);
        Fc2World world = WorldLoader.Load(map, readByPath, progress);
        ArchetypeIndex archetypes = ArchetypeIndex.Load(worldName, readByPath, progress);
        List<NavMeshObstacle> obstacles = NavMeshObstacle.Of(world.Entities, archetypes);
        Dictionary<int, float> water = terrain.Water.ToDictionary(w => w.SectorId, w => w.Level);

        progress?.Report($"Generating {map.Sectors.Count} sectors around {obstacles.Count} obstacles");
        return (Build(map.SectorsPerSide, map.Sectors.Select(s => s.SectorId), terrain.HeightMetersAt,
            obstacles, id => water.TryGetValue(id, out float level) ? level : NvmSector.NoWater), obstacles.Count);
    }

    /// <summary>
    /// Sets <c>HasNavMesh</c> on these sectors' entries of a <c>sectorsdep.fcb</c>, the flag without
    /// which the game never opens a sector's navmesh file. Returns how many entries it set.
    /// </summary>
    public static int MarkHasNavMesh(FcbObject sectorsDep, IReadOnlySet<int> sectorIds)
    {
        uint worldSector = FcbClassDefinitions.Crc32Ascii("CWorldSector");
        uint sectorId = FcbClassDefinitions.Crc32Ascii("SectorId");
        uint hasNavMesh = FcbClassDefinitions.Crc32Ascii("HasNavMesh");
        int marked = 0;
        foreach (FcbObject group in sectorsDep.Children)
        {
            foreach (FcbObject sector in group.Children.Where(s => s.TypeHash == worldSector))
            {
                if (FcbEntityFields.ReadU32(sector, sectorId) is uint id && sectorIds.Contains((int)id))
                {
                    sector.Values[hasNavMesh] = [1];
                    marked++;
                }
            }
        }
        return marked;
    }

    private sealed class Cell(float x, float y, float size)
    {
        public readonly float X = x, Y = y, Size = size;
        public Cell[]? Children;
        public bool Blocked;
    }

    private readonly record struct Triangle(Vector3 A, Vector3 B, Vector3 C);

    /// <param name="heightAt">Terrain height at a world position.</param>
    /// <param name="waterLevelOf">A sector's water surface, or <see cref="NvmSector.NoWater"/>.</param>
    public static Dictionary<int, NvmSector> Build(
        int sectorsPerSide, IEnumerable<int> sectorIds, Func<float, float, float> heightAt,
        IReadOnlyList<NavMeshObstacle> obstacles, Func<int, float> waterLevelOf, ulong buildId = World1BuildId)
    {
        var roots = new Dictionary<int, Cell>();
        foreach (int id in sectorIds)
        {
            var root = new Cell(id % sectorsPerSide * SectorSize, id / sectorsPerSide * SectorSize, SectorSize);
            roots[id] = root;
            Refine(root, heightAt, obstacles);
        }

        Cell? Find(Vector2 p)
        {
            int column = (int)MathF.Floor(p.X / SectorSize), row = (int)MathF.Floor(p.Y / SectorSize);
            if (column < 0 || row < 0 || column >= sectorsPerSide || !roots.TryGetValue(row * sectorsPerSide + column, out Cell? cell))
            {
                return null;
            }
            while (cell.Children is { } children)
            {
                float half = cell.Size / 2;
                cell = children[(p.X >= cell.X + half ? 1 : 0) + (p.Y >= cell.Y + half ? 2 : 0)];
            }
            return cell;
        }

        Balance(roots.Values, Find, heightAt, obstacles);

        var triangles = new Dictionary<int, List<Triangle>>();
        foreach ((int id, Cell root) in roots)
        {
            var into = triangles[id] = [];
            foreach (Cell leaf in Leaves(root).Where(c => !c.Blocked))
            {
                Fan(leaf, Find, heightAt, into);
            }
        }

        return Assemble(sectorsPerSide, triangles, waterLevelOf, buildId);
    }

    private static void Refine(Cell cell, Func<float, float, float> heightAt, IReadOnlyList<NavMeshObstacle> obstacles)
    {
        bool partial = false;
        foreach (NavMeshObstacle obstacle in obstacles)
        {
            if (!obstacle.Overlaps(cell.X, cell.Y, cell.Size))
            {
                continue;
            }
            if (Corners(cell).All(obstacle.Contains))
            {
                cell.Blocked = true;
                return;
            }
            partial = true;
        }

        if (cell.Size > MaxCell || (cell.Size > MinCell && (partial || Bends(cell, heightAt))))
        {
            Split(cell, heightAt, obstacles);
        }
        else
        {
            cell.Blocked = partial;
        }
    }

    private static void Split(Cell cell, Func<float, float, float> heightAt, IReadOnlyList<NavMeshObstacle> obstacles)
    {
        float half = cell.Size / 2;
        cell.Children =
        [
            new(cell.X, cell.Y, half), new(cell.X + half, cell.Y, half),
            new(cell.X, cell.Y + half, half), new(cell.X + half, cell.Y + half, half),
        ];
        foreach (Cell child in cell.Children)
        {
            if (cell.Blocked)
            {
                child.Blocked = true;
            }
            else
            {
                Refine(child, heightAt, obstacles);
            }
        }
        cell.Blocked = false;
    }

    /// <summary>Whether the terrain inside strays from the two triangles over the cell's corners.</summary>
    private static bool Bends(Cell cell, Func<float, float, float> heightAt)
    {
        if (cell.Size <= 1)
        {
            return false;
        }
        float h00 = heightAt(cell.X, cell.Y), h10 = heightAt(cell.X + cell.Size, cell.Y);
        float h01 = heightAt(cell.X, cell.Y + cell.Size), h11 = heightAt(cell.X + cell.Size, cell.Y + cell.Size);
        for (float dy = 0; dy <= cell.Size; dy++)
        {
            for (float dx = 0; dx <= cell.Size; dx++)
            {
                float u = dx / cell.Size, v = dy / cell.Size;
                float flat = u >= v ? h00 + (h10 - h00) * u + (h11 - h10) * v : h00 + (h11 - h01) * u + (h01 - h00) * v;
                if (MathF.Abs(heightAt(cell.X + dx, cell.Y + dy) - flat) > MaxHeightError)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Splits any cell more than twice the size of a neighbour, until none is.</summary>
    private static void Balance(
        IEnumerable<Cell> roots, Func<Vector2, Cell?> find, Func<float, float, float> heightAt,
        IReadOnlyList<NavMeshObstacle> obstacles)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (Cell leaf in roots.SelectMany(Leaves).ToList())
            {
                if (leaf.Size > MinCell && Neighbours(leaf, MinCell, find).Any(n => n.Size < leaf.Size / 2))
                {
                    Split(leaf, heightAt, obstacles);
                    changed = true;
                }
            }
        }
    }

    /// <summary>The leaves across each side, probed every <paramref name="step"/> just outside it.</summary>
    private static IEnumerable<Cell> Neighbours(Cell cell, float step, Func<Vector2, Cell?> find)
    {
        const float outside = MinCell / 4;
        for (float t = step / 2; t < cell.Size; t += step)
        {
            foreach (Vector2 probe in new Vector2[]
            {
                new(cell.X + t, cell.Y - outside), new(cell.X + cell.Size + outside, cell.Y + t),
                new(cell.X + t, cell.Y + cell.Size + outside), new(cell.X - outside, cell.Y + t),
            })
            {
                if (find(probe) is { } neighbour)
                {
                    yield return neighbour;
                }
            }
        }
    }

    private static void Fan(Cell cell, Func<Vector2, Cell?> find, Func<float, float, float> heightAt, List<Triangle> into)
    {
        float s = cell.Size, q = s / 4;
        const float outside = MinCell / 4;
        bool Finer(Vector2 probe) => find(probe) is { } n && n.Size < s;

        // Counter-clockwise from the bottom-left corner, a side's midpoint where a finer cell meets it.
        var ring = new List<Vector2> { new(cell.X, cell.Y) };
        if (Finer(new(cell.X + q, cell.Y - outside))) ring.Add(new(cell.X + s / 2, cell.Y));
        ring.Add(new(cell.X + s, cell.Y));
        if (Finer(new(cell.X + s + outside, cell.Y + q))) ring.Add(new(cell.X + s, cell.Y + s / 2));
        ring.Add(new(cell.X + s, cell.Y + s));
        if (Finer(new(cell.X + s - q, cell.Y + s + outside))) ring.Add(new(cell.X + s / 2, cell.Y + s));
        ring.Add(new(cell.X, cell.Y + s));
        if (Finer(new(cell.X - outside, cell.Y + s - q))) ring.Add(new(cell.X, cell.Y + s / 2));

        Vector3 At(Vector2 p) => new(p.X, p.Y, heightAt(p.X, p.Y));
        if (ring.Count == 4)
        {
            into.Add(new(At(ring[0]), At(ring[1]), At(ring[2])));
            into.Add(new(At(ring[0]), At(ring[2]), At(ring[3])));
            return;
        }
        Vector3 centre = At(new(cell.X + s / 2, cell.Y + s / 2));
        for (int i = 0; i < ring.Count; i++)
        {
            into.Add(new(centre, At(ring[i]), At(ring[(i + 1) % ring.Count])));
        }
    }

    private static IEnumerable<Vector2> Corners(Cell c) =>
        [new(c.X, c.Y), new(c.X + c.Size, c.Y), new(c.X + c.Size, c.Y + c.Size), new(c.X, c.Y + c.Size)];

    private static IEnumerable<Cell> Leaves(Cell cell)
        => cell.Children is { } children ? children.SelectMany(Leaves) : [cell];

    /// <summary>Packs each sector's triangles and derives what the engine reads alongside them.</summary>
    private static Dictionary<int, NvmSector> Assemble(
        int sectorsPerSide, Dictionary<int, List<Triangle>> triangles, Func<int, float> waterLevelOf, ulong buildId)
    {
        // Per sector: the header shell to pack against, the triangles' corners as unpacked positions.
        var shells = new Dictionary<int, NvmSector>();
        var corners = new Dictionary<int, (PackedVector[] Packed, Vector3[] Unpacked, uint[] Indices)>();
        foreach ((int id, List<Triangle> list) in triangles)
        {
            if (list.Count >= ushort.MaxValue)
            {
                throw new InvalidOperationException($"Sector {id} needs {list.Count} triangles; a link holds 65535.");
            }
            float minX = id % sectorsPerSide * SectorSize, minY = id / sectorsPerSide * SectorSize;
            NvmSector shell = shells[id] = new NvmSector
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

            var index = new Dictionary<PackedVector, uint>();
            var packed = new List<PackedVector>();
            uint IndexOf(Vector3 v)
            {
                PackedVector p = shell.Pack(v);
                if (!index.TryGetValue(p, out uint i))
                {
                    index[p] = i = (uint)packed.Count;
                    packed.Add(p);
                }
                return i;
            }
            uint[] indices = [.. list.SelectMany(t => new[] { IndexOf(t.A), IndexOf(t.B), IndexOf(t.C) })];
            corners[id] = ([.. packed], [.. packed.Select(shell.Unpack)], indices);
        }

        // Each directed edge, keyed by its ends in world space, so a seam finds its neighbour.
        static (long, long, long, long) Key(Vector3 a, Vector3 b)
            => ((long)MathF.Round(a.X * 256), (long)MathF.Round(a.Y * 256), (long)MathF.Round(b.X * 256), (long)MathF.Round(b.Y * 256));
        var owner = new Dictionary<(long, long, long, long), uint>();
        foreach ((int id, var c) in corners)
        {
            for (int n = 0; n < c.Indices.Length / 3; n++)
            {
                for (int k = 0; k < 3; k++)
                {
                    owner[Key(c.Unpacked[c.Indices[n * 3 + k]], c.Unpacked[c.Indices[n * 3 + (k + 1) % 3]])] =
                        (uint)n << 16 | (uint)id;
                }
            }
        }

        var links = corners.ToDictionary(p => p.Key, p =>
        {
            var c = p.Value;
            var result = new uint[c.Indices.Length];
            for (int i = 0; i < result.Length; i++)
            {
                int n = i / 3, k = i % 3;
                result[i] = owner.TryGetValue(Key(c.Unpacked[c.Indices[n * 3 + (k + 1) % 3]], c.Unpacked[c.Indices[i]]),
                    out uint neighbour) ? neighbour : NvmSector.NoLink;
            }
            return result;
        });

        Dictionary<uint, ushort> graphIds = GraphIds(links);
        var clearance = new BoundaryIndex(corners.SelectMany(p => Enumerable.Range(0, p.Value.Indices.Length)
            .Where(i => links[p.Key][i] == NvmSector.NoLink)
            .Select(i => (p.Value.Unpacked[p.Value.Indices[i]], p.Value.Unpacked[p.Value.Indices[i / 3 * 3 + (i % 3 + 1) % 3]]))));

        var sectors = new Dictionary<int, NvmSector>();
        foreach ((int id, var c) in corners)
        {
            NvmSector shell = shells[id];
            int count = c.Indices.Length / 3;
            var nodes = new NvmNode[count];
            var flat = new (Vector2, Vector2, Vector2)[count];
            for (int n = 0; n < count; n++)
            {
                uint ia = c.Indices[n * 3], ib = c.Indices[n * 3 + 1], ic = c.Indices[n * 3 + 2];
                Vector3 a = c.Unpacked[ia], b = c.Unpacked[ib], cc = c.Unpacked[ic];
                Vector3 centroid = (a + b + cc) / 3;
                Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, cc - a));
                var radii = new NvmClearance();
                Vector3[] samples = NvmNode.SamplePoints(centroid, a, b, cc);
                for (int s = 0; s < samples.Length; s++)
                {
                    radii[s] = (byte)(int)(MathF.Min(clearance.DistanceTo(samples[s]), MaxClearance) * 25);
                }
                float water = shell.WaterLevel - UnderWaterDepth + 0.001f;
                nodes[n] = new NvmNode(graphIds[(uint)n << 16 | (uint)id], 3, (uint)n * 3, shell.Pack(centroid),
                    Normal(normal.X), Normal(normal.Y), Normal(normal.Z),
                    a.Z <= water && b.Z <= water && cc.Z <= water, false, ia, ib, ic, radii);
                flat[n] = (new(a.X, a.Y), new(b.X, b.Y), new(cc.X, cc.Y));
            }

            (NvmQuad[] quads, ushort[] quadNodes) =
                NvmSectorFile.BuildQuadTree(shell.MinX, shell.MinY, shell.MaxX, shell.MaxY, flat);
            if (quadNodes.Length > ushort.MaxValue)
            {
                throw new InvalidOperationException($"Sector {id}'s quadtree lists {quadNodes.Length} entries; a quad's start holds 65535.");
            }
            sectors[id] = shell with
            {
                Links = links[id],
                Nodes = nodes,
                Vertices = c.Packed,
                Quads = quads,
                QuadNodes = quadNodes,
            };
        }
        return sectors;

        static sbyte Normal(float component) => (sbyte)MathF.Round(component * 127);
    }

    /// <summary>Numbers the linked regions from 1, largest first, as <c>SetGraphIdToNodeAndFriends</c> floods them.</summary>
    private static Dictionary<uint, ushort> GraphIds(Dictionary<int, uint[]> links)
    {
        var regions = new List<List<uint>>();
        var seen = new HashSet<uint>();
        foreach ((int id, uint[] sectorLinks) in links)
        {
            for (int n = 0; n < sectorLinks.Length / 3; n++)
            {
                uint start = (uint)n << 16 | (uint)id;
                if (!seen.Add(start))
                {
                    continue;
                }
                var region = new List<uint>();
                var stack = new Stack<uint>([start]);
                while (stack.TryPop(out uint node))
                {
                    region.Add(node);
                    uint[] around = links[(int)(node & 0xFFFF)];
                    int first = (int)(node >> 16) * 3;
                    for (int k = 0; k < 3; k++)
                    {
                        if (around[first + k] is var next && next != NvmSector.NoLink && seen.Add(next))
                        {
                            stack.Push(next);
                        }
                    }
                }
                regions.Add(region);
            }
        }

        var ids = new Dictionary<uint, ushort>();
        int graph = 1;
        foreach (List<uint> region in regions.OrderByDescending(r => r.Count))
        {
            foreach (uint node in region)
            {
                ids[node] = (ushort)Math.Min(graph, ushort.MaxValue - 1);
            }
            graph++;
        }
        return ids;
    }

    /// <summary>The navmesh's open edges, bucketed so a point finds those within reach.</summary>
    private sealed class BoundaryIndex
    {
        private readonly Dictionary<(int, int), List<(Vector3 A, Vector3 B)>> _buckets = [];

        public BoundaryIndex(IEnumerable<(Vector3 A, Vector3 B)> edges)
        {
            foreach ((Vector3 a, Vector3 b) in edges)
            {
                (int x0, int y0) = Bucket(Vector3.Min(a, b));
                (int x1, int y1) = Bucket(Vector3.Max(a, b));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!_buckets.TryGetValue((x, y), out var list))
                        {
                            _buckets[(x, y)] = list = [];
                        }
                        list.Add((a, b));
                    }
                }
            }
        }

        public float DistanceTo(Vector3 p)
        {
            float best = MaxClearance;
            (int x0, int y0) = Bucket(p - new Vector3(MaxClearance));
            (int x1, int y1) = Bucket(p + new Vector3(MaxClearance));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!_buckets.TryGetValue((x, y), out var list))
                    {
                        continue;
                    }
                    foreach ((Vector3 a, Vector3 b) in list)
                    {
                        Vector3 ab = b - a;
                        float t = Math.Clamp(Vector3.Dot(p - a, ab) / ab.LengthSquared(), 0, 1);
                        best = MathF.Min(best, Vector3.Distance(p, a + ab * t));
                    }
                }
            }
            return best;
        }

        private static (int, int) Bucket(Vector3 p)
            => ((int)MathF.Floor(p.X / MaxClearance), (int)MathF.Floor(p.Y / MaxClearance));
    }
}
