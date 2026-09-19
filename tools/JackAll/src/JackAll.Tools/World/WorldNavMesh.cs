using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>One decoded sector file, every index still local to it.</summary>
public sealed class NavMeshSector
{
    /// <summary>A link slot with no neighbour behind that edge.</summary>
    public const uint NoLink = 0xFFFEFFFF;

    public required int SectorId { get; init; }

    public required Vector3[] Vertices { get; init; }

    /// <summary>Three indices into <see cref="Vertices"/> per node; edge k runs from corner k to
    /// corner k + 1.</summary>
    public required int[] Triangles { get; init; }

    public required Vector3[] Centroids { get; init; }

    /// <summary>Node n's links are <c>Links[LinkStart[n]..LinkStart[n + 1])</c>, the first three
    /// being its edges in order. Each is <c>(nodeIndex &lt;&lt; 16) | sectorId</c> or
    /// <see cref="NoLink"/>.</summary>
    public required int[] LinkStart { get; init; }

    public required uint[] Links { get; init; }

    public int NodeCount => Triangles.Length / 3;
}

/// <summary>A map's navmesh with every index in one world-wide vertex and node space.</summary>
public sealed class NavMesh
{
    public required Vector3[] Vertices { get; init; }

    /// <summary>Three vertex indices per node.</summary>
    public required int[] Triangles { get; init; }

    /// <summary>One per node, where the engine places it.</summary>
    public required Vector3[] Centroids { get; init; }

    /// <summary>Vertex index pairs for edges two triangles of one sector share, each once.</summary>
    public required int[] InteriorEdges { get; init; }

    /// <summary>Vertex index pairs for edges with nothing walkable beyond them.</summary>
    public required int[] BoundaryEdges { get; init; }

    /// <summary>Vertex index pairs for edges whose neighbour lies in another sector.</summary>
    public required int[] SeamEdges { get; init; }

    /// <summary>Node index pairs, one per neighbouring pair of triangles.</summary>
    public required int[] Links { get; init; }

    public required int SectorCount { get; init; }

    public int NodeCount => Triangles.Length / 3;

    public int LinkCount => Links.Length / 2;
}

/// <summary>
/// The AI navigation mesh a map ships, read from the per-sector <c>nv_&lt;id&gt;.nvm</c> files.
/// Only campaign levels have them, so a world loads whichever of its sectors do.
/// </summary>
/// <remarks>
/// Positions are quantised: <c>CNavArchive::SetPackedVectorSettings</c> makes the sector's own
/// bounding box the basis, so X and Y are <c>int16</c> steps of <c>1 / (1 &lt;&lt; (15 - log2(span)))</c>
/// around its centre while Z is a plain <c>int16</c> in 1/32 metre. Decoding a vertex therefore
/// needs its sector's header, never the world grid.
/// </remarks>
public static class WorldNavMesh
{
    /// <summary>The per-sector tag, "hMvN" on disk.</summary>
    private const uint Magic = 0x4E764D68;

    /// <summary>Serialized size of one <c>CNavMeshNode</c>, which is smaller than its 60-byte
    /// in-memory form.</summary>
    private const int NodeSize = 48;

    private const int LinkCountOffset = 0x0A;
    private const int LinkOffsetOffset = 0x0B;
    private const int PositionOffset = 0x0F;
    private const int VertexIndexOffset = 0x1A;

    /// <summary>Where the link block's count sits, past the header and the sector scalars.</summary>
    private const int LinkBlockOffset = 0x24 + 4 + 8 + 2;

    private const int PackedVectorSize = 6;

    /// <summary>Format version that introduced this node layout; everything retail ships is newer.</summary>
    private const uint FirstKnownVersion = 0x13900;

    private const float HeightStep = 1f / 32f;

    /// <summary>A resolved link with nothing walkable behind it.</summary>
    private const int NoNeighbour = -1;

    /// <summary>A resolved link whose sector was not loaded.</summary>
    private const int Elsewhere = -2;

    public static NavMesh Load(
        TerrainMap map, Func<string, byte[]?> readByPath, IProgress<string>? progress = null)
    {
        progress?.Report($"Loading {map.Name} navmesh");

        var perSector = new NavMeshSector?[map.Sectors.Count];
        Parallel.For(0, map.Sectors.Count, index =>
        {
            (string path, int sectorId) = map.Sectors[index];
            string file = path
                .Replace(@"\sdat\", @"\nv\sectors\", StringComparison.OrdinalIgnoreCase)
                .Replace($"sd{sectorId}.sdat", $"nv_{sectorId}.nvm", StringComparison.OrdinalIgnoreCase);

            if (readByPath(file) is { } bytes)
            {
                perSector[index] = ReadSector(bytes);
            }
        });

        NavMesh mesh = Assemble([.. perSector.OfType<NavMeshSector>()]);
        progress?.Report(
            $"Loaded {map.Name} navmesh: {mesh.NodeCount:N0} triangles across {mesh.SectorCount:N0} sectors");
        return mesh;
    }

    /// <summary>Reads one sector file, or returns null if it is not a navmesh this can decode.</summary>
    public static NavMeshSector? ReadSector(byte[] b)
    {
        if (b.Length < LinkBlockOffset + 4 || BitConverter.ToUInt32(b, 4) != Magic ||
            BitConverter.ToUInt32(b, 8) < FirstKnownVersion)
        {
            return null;
        }

        float minX = BitConverter.ToSingle(b, 0x14), minY = BitConverter.ToSingle(b, 0x18);
        float maxX = BitConverter.ToSingle(b, 0x1C), maxY = BitConverter.ToSingle(b, 0x20);
        uint span = (uint)MathF.Round(MathF.Max(maxX - minX, maxY - minY)) * 2;
        if (!float.IsFinite(minX) || !float.IsFinite(minY) || span == 0 || (span & (span - 1)) != 0)
        {
            return null;
        }

        var centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        float step = 1f / (1 << (15 - BitOperations.Log2(span)));
        Vector3 Unpack(int p) => new(
            BitConverter.ToInt16(b, p) * step + centre.X,
            BitConverter.ToInt16(b, p + 2) * step + centre.Y,
            BitConverter.ToInt16(b, p + 4) * HeightStep);

        int at = LinkBlockOffset;
        long blockCount = BitConverter.ToUInt32(b, at);
        at += 4;
        if (at + blockCount * 4 + 4 > b.Length)
        {
            return null;
        }
        int blockStart = at;
        at += (int)blockCount * 4;

        long count = BitConverter.ToUInt32(b, at);
        at += 4;
        if (count == 0 || at + count * NodeSize + 4 > b.Length)
        {
            return null;
        }
        int nodeStart = at;
        at += (int)count * NodeSize;

        long vertexCount = BitConverter.ToUInt32(b, at);
        at += 4;
        if (at + vertexCount * PackedVectorSize > b.Length)
        {
            return null;
        }

        var vertices = new Vector3[vertexCount];
        for (int v = 0; v < vertices.Length; v++)
        {
            vertices[v] = Unpack(at + v * PackedVectorSize);
        }

        var triangles = new int[count * 3];
        var centroids = new Vector3[count];
        var linkStart = new int[count + 1];
        var links = new List<uint>((int)count * 3);
        for (int n = 0; n < count; n++)
        {
            int p = nodeStart + n * NodeSize;
            centroids[n] = Unpack(p + PositionOffset);
            for (int k = 0; k < 3; k++)
            {
                uint corner = BitConverter.ToUInt32(b, p + VertexIndexOffset + k * 4);
                if (corner >= vertexCount)
                {
                    return null;
                }
                triangles[n * 3 + k] = (int)corner;
            }

            linkStart[n] = links.Count;
            long first = BitConverter.ToUInt32(b, p + LinkOffsetOffset);
            long last = first + b[p + LinkCountOffset];
            if (last > blockCount)
            {
                return null;
            }
            for (long l = first; l < last; l++)
            {
                links.Add(BitConverter.ToUInt32(b, blockStart + (int)l * 4));
            }
        }
        linkStart[count] = links.Count;

        return new NavMeshSector
        {
            SectorId = BitConverter.ToUInt16(b, 0x12),
            Vertices = vertices,
            Triangles = triangles,
            Centroids = centroids,
            LinkStart = linkStart,
            Links = [.. links],
        };
    }

    /// <summary>Joins sectors into one index space and sorts every triangle edge into interior,
    /// boundary or seam, resolving links that cross into a neighbouring sector.</summary>
    public static NavMesh Assemble(IReadOnlyList<NavMeshSector> sectors)
    {
        var vertexBase = new int[sectors.Count];
        var nodeBase = new int[sectors.Count + 1];
        var linkBase = new int[sectors.Count];
        var sectorById = new Dictionary<int, int>(sectors.Count);
        int vertexTotal = 0, nodeTotal = 0, linkTotal = 0;
        for (int s = 0; s < sectors.Count; s++)
        {
            vertexBase[s] = vertexTotal;
            nodeBase[s] = nodeTotal;
            linkBase[s] = linkTotal;
            sectorById[sectors[s].SectorId] = s;
            vertexTotal += sectors[s].Vertices.Length;
            nodeTotal += sectors[s].NodeCount;
            linkTotal += sectors[s].Links.Length;
        }
        nodeBase[sectors.Count] = nodeTotal;

        var vertices = new Vector3[vertexTotal];
        var triangles = new int[nodeTotal * 3];
        var centroids = new Vector3[nodeTotal];
        var linkStart = new int[nodeTotal + 1];
        var resolved = new int[linkTotal];
        for (int s = 0; s < sectors.Count; s++)
        {
            NavMeshSector sector = sectors[s];
            sector.Vertices.CopyTo(vertices, vertexBase[s]);
            sector.Centroids.CopyTo(centroids, nodeBase[s]);
            for (int i = 0; i < sector.Triangles.Length; i++)
            {
                triangles[nodeBase[s] * 3 + i] = sector.Triangles[i] + vertexBase[s];
            }
            for (int n = 0; n < sector.NodeCount; n++)
            {
                linkStart[nodeBase[s] + n] = linkBase[s] + sector.LinkStart[n];
            }
            for (int l = 0; l < sector.Links.Length; l++)
            {
                resolved[linkBase[s] + l] = Resolve(sector.Links[l]);
            }
        }
        linkStart[nodeTotal] = linkTotal;

        int Resolve(uint link)
        {
            if (link == NavMeshSector.NoLink)
            {
                return NoNeighbour;
            }
            if (!sectorById.TryGetValue((int)(link & 0xFFFF), out int target))
            {
                return Elsewhere;
            }
            int index = (int)(link >> 16);
            return index < sectors[target].NodeCount ? nodeBase[target] + index : NoNeighbour;
        }

        bool LinksBack(int from, int to, int slots) =>
            Array.IndexOf(resolved, to, linkStart[from], Math.Min(slots, linkStart[from + 1] - linkStart[from])) >= 0;

        static void Add(List<int> into, int a, int b)
        {
            into.Add(a);
            into.Add(b);
        }

        var interior = new List<int>(nodeTotal * 3 / 2);
        var boundary = new List<int>();
        var seam = new List<int>();
        var pairs = new List<int>(nodeTotal * 3);
        for (int s = 0; s < sectors.Count; s++)
        {
            for (int n = nodeBase[s]; n < nodeBase[s + 1]; n++)
            {
                int first = linkStart[n], links = linkStart[n + 1] - first;
                for (int k = 0; k < 3; k++)
                {
                    int m = k < links ? resolved[first + k] : NoNeighbour;
                    int a = triangles[n * 3 + k], b = triangles[n * 3 + (k + 1) % 3];
                    if (m == NoNeighbour)
                    {
                        Add(boundary, a, b);
                    }
                    else if (m == Elsewhere || m < nodeBase[s] || m >= nodeBase[s + 1])
                    {
                        Add(seam, a, b);
                    }
                    // A shared edge is drawn from its lower node unless the other side never links back.
                    else if (n < m || !LinksBack(m, n, 3))
                    {
                        Add(interior, a, b);
                    }
                }

                for (int l = first; l < linkStart[n + 1]; l++)
                {
                    int m = resolved[l];
                    if (m >= 0 && m != n && Array.IndexOf(resolved, m, first, l - first) < 0 &&
                        (n < m || !LinksBack(m, n, int.MaxValue)))
                    {
                        Add(pairs, n, m);
                    }
                }
            }
        }

        return new NavMesh
        {
            Vertices = vertices,
            Triangles = triangles,
            Centroids = centroids,
            InteriorEdges = [.. interior],
            BoundaryEdges = [.. boundary],
            SeamEdges = [.. seam],
            Links = [.. pairs],
            SectorCount = sectors.Count,
        };
    }
}
