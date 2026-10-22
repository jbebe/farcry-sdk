using System.Numerics;
using System.Runtime.CompilerServices;
using JackAll.Core.Format;

namespace JackAll.Tools.World;

/// <summary>A position quantised against its sector's bounding box; see <see cref="NvmSector.Unpack"/>.</summary>
public readonly record struct PackedVector(short X, short Y, short Z);

/// <summary>Ten clearance radii in 4 cm steps, at the points <see cref="NvmNode.SamplePoints"/> lists.</summary>
[InlineArray(10)]
public struct NvmClearance
{
    private byte _first;
}

/// <summary>One triangle of a sector's navmesh.</summary>
/// <param name="LinkOffset">Index of the node's first entry in <see cref="NvmSector.Links"/>.</param>
/// <param name="A">First corner, an index into <see cref="NvmSector.Vertices"/>; edge k runs from
/// corner k to corner k + 1.</param>
public record struct NvmNode(
    ushort GraphId, byte LinkCount, uint LinkOffset, PackedVector Centroid,
    sbyte NormalX, sbyte NormalY, sbyte NormalZ, bool UnderWater, bool AboveTerrain,
    uint A, uint B, uint C, NvmClearance Clearance)
{
    /// <summary>The points <c>ComputeSectorRadii</c> measures clearance at, in slot order.</summary>
    public static Vector3[] SamplePoints(Vector3 centroid, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = (a + b) / 2, ac = (a + c) / 2, bc = (b + c) / 2;
        return [centroid, a, b, c, ab, ac, bc, (ab + ac) / 2, (ab + bc) / 2, (ac + bc) / 2];
    }
}

/// <param name="Height">In 8 mm steps.</param>
/// <param name="Vertex">Index into <see cref="NvmSector.CoverVertices"/>.</param>
public readonly record struct NvmCover(byte Height, ushort Vertex, byte Type, ushort Node);

public readonly record struct NvmDynamicCover(NvmCover Cover, uint First, uint Second);

/// <summary>One quad of a sector's spatial index over its nodes.</summary>
/// <param name="Start">First index into <see cref="NvmSector.QuadNodes"/>.</param>
/// <param name="Children">Four quad indices, <c>0xFFFF</c> for a child pruned for being empty; null
/// on a leaf.</param>
public readonly record struct NvmQuad(
    float MinX, float MaxX, float MinY, float MaxY, ushort Count, ushort Start, ushort[]? Children);

/// <summary>
/// One <c>nv_&lt;id&gt;.nvm</c> as stored, every field kept, so a decode and encode reproduce the
/// file. The layout is in docs/docs/file-formats/nvm.md.
/// </summary>
public sealed record NvmSector
{
    /// <summary>A link slot with no neighbour behind that edge.</summary>
    public const uint NoLink = 0xFFFEFFFF;

    /// <summary><c>(column &lt;&lt; 7) | row</c>; retail leaves junk in the top two bits.</summary>
    public required ushort SectorWord { get; init; }
    public required ushort SectorId { get; init; }
    public required float MinX { get; init; }
    public required float MinY { get; init; }
    public required float MaxX { get; init; }
    public required float MaxY { get; init; }

    /// <summary><see cref="NoWater"/> when the sector has none.</summary>
    public required float WaterLevel { get; init; }

    /// <summary>The same in every sector of a world.</summary>
    public required ulong BuildId { get; init; }

    /// <summary><c>0x7F7F</c> in every retail sector; meaning unknown.</summary>
    public required ushort Unknown30 { get; init; }

    /// <summary>Every node's neighbours: <c>(node index &lt;&lt; 16) | sector id</c>, or <see cref="NoLink"/>.</summary>
    public required uint[] Links { get; init; }
    public required NvmNode[] Nodes { get; init; }
    public required PackedVector[] Vertices { get; init; }
    public required NvmCover[] Covers { get; init; }
    public required NvmDynamicCover[] DynamicCovers { get; init; }
    public required PackedVector[] CoverVertices { get; init; }
    public required NvmQuad[] Quads { get; init; }
    public required ushort[] QuadNodes { get; init; }

    public const float NoWater = -1e6f;

    public Vector2 Centre => new((MinX + MaxX) * 0.5f, (MinY + MaxY) * 0.5f);

    /// <summary>Packed X and Y steps per metre: <c>1 &lt;&lt; (15 - log2(span))</c>.</summary>
    public float Scale
    {
        get
        {
            uint span = (uint)MathF.Round(MathF.Max(MaxX - MinX, MaxY - MinY)) * 2;
            return 1 << (15 - BitOperations.Log2(span));
        }
    }

    public Vector3 Unpack(PackedVector p)
    {
        float step = 1f / Scale;
        Vector2 centre = Centre;
        return new(p.X * step + centre.X, p.Y * step + centre.Y, p.Z * NvmSectorFile.HeightStep);
    }

    /// <summary>Truncates toward zero, as <c>CNavArchive</c> does.</summary>
    public PackedVector Pack(Vector3 v)
    {
        float scale = Scale;
        Vector2 centre = Centre;
        return new((short)(int)((v.X - centre.X) * scale), (short)(int)((v.Y - centre.Y) * scale),
            (short)(int)(v.Z * 32f));
    }
}

public static class NvmSectorFile
{
    private const uint Magic = 0x4E764D68;

    /// <summary>The only version retail ships, and the only one whose layout this reads.</summary>
    public const uint Version = 0x14100;

    public const float HeightStep = 1f / 32f;

    /// <summary>A node's in-memory size, which the file records though it stores 48 bytes.</summary>
    private const uint NodeObjectSize = 60;

    /// <summary><c>CNavMeshSector::GetReloadSize</c>: the sector object plus each node.</summary>
    private const uint SectorObjectSize = 120;

    /// <summary>Quads stop splitting at this half-width, so a 64 m sector always has 341.</summary>
    private const float MinQuadHalfWidth = 4f;

    public static NvmSector Decode(ReadOnlySpan<byte> bytes)
    {
        var c = new ByteCursor(bytes);
        if (c.ReadU32() != 0 || c.ReadU32() != Magic || c.ReadU32() != Version)
        {
            throw new InvalidDataException("Not a navmesh sector of the version retail ships.");
        }
        c.ReadU32();

        ushort sectorWord = c.ReadU16(), sectorId = c.ReadU16();
        float minX = c.ReadF32(), minY = c.ReadF32(), maxX = c.ReadF32(), maxY = c.ReadF32();
        float water = c.ReadF32();
        ulong buildId = c.ReadU32() | (ulong)c.ReadU32() << 32;
        ushort unknown30 = c.ReadU16();
        uint[] links = c.ReadU32Array((int)c.ReadU32());

        var nodes = new NvmNode[c.ReadU32()];
        for (int i = 0; i < nodes.Length; i++)
        {
            if (c.ReadU32() != NodeObjectSize || c.ReadU16() != sectorId || c.ReadU16() != i)
            {
                throw new InvalidDataException($"Node {i} does not name its own sector and index.");
            }
            ushort graphId = c.ReadU16();
            byte linkCount = c.ReadU8();
            uint linkOffset = c.ReadU32();
            PackedVector centroid = ReadPacked(ref c);
            sbyte nx = (sbyte)c.ReadU8(), ny = (sbyte)c.ReadU8(), nz = (sbyte)c.ReadU8();
            bool underWater = c.ReadU8() != 0, aboveTerrain = c.ReadU8() != 0;
            uint a = c.ReadU32(), b = c.ReadU32(), cc = c.ReadU32();
            var clearance = new NvmClearance();
            c.ReadSpan(10).CopyTo(clearance);
            nodes[i] = new NvmNode(graphId, linkCount, linkOffset, centroid, nx, ny, nz, underWater,
                aboveTerrain, a, b, cc, clearance);
        }

        PackedVector[] vertices = ReadPackedArray(ref c);
        var covers = new NvmCover[c.ReadU32()];
        for (int i = 0; i < covers.Length; i++)
        {
            covers[i] = ReadCover(ref c);
        }
        var dynamicCovers = new NvmDynamicCover[c.ReadU32()];
        for (int i = 0; i < dynamicCovers.Length; i++)
        {
            dynamicCovers[i] = new NvmDynamicCover(ReadCover(ref c), c.ReadU32(), c.ReadU32());
        }
        PackedVector[] coverVertices = ReadPackedArray(ref c);

        var quads = new NvmQuad[c.ReadU32()];
        for (int i = 0; i < quads.Length; i++)
        {
            bool split = c.ReadU32() != 0;
            float qMinX = c.ReadF32(), qMaxX = c.ReadF32(), qMinY = c.ReadF32(), qMaxY = c.ReadF32();
            ushort count = c.ReadU16(), start = c.ReadU16();
            ushort[]? children = split ? [c.ReadU16(), c.ReadU16(), c.ReadU16(), c.ReadU16()] : null;
            quads[i] = new NvmQuad(qMinX, qMaxX, qMinY, qMaxY, count, start, children);
        }
        ushort[] quadNodes = c.ReadU16Array((int)c.ReadU32());
        if (c.Remaining != 0)
        {
            throw new InvalidDataException($"{c.Remaining} bytes follow the quadtree.");
        }

        return new NvmSector
        {
            SectorWord = sectorWord,
            SectorId = sectorId,
            MinX = minX,
            MinY = minY,
            MaxX = maxX,
            MaxY = maxY,
            WaterLevel = water,
            BuildId = buildId,
            Unknown30 = unknown30,
            Links = links,
            Nodes = nodes,
            Vertices = vertices,
            Covers = covers,
            DynamicCovers = dynamicCovers,
            CoverVertices = coverVertices,
            Quads = quads,
            QuadNodes = quadNodes,
        };
    }

    public static byte[] Encode(NvmSector sector)
    {
        var w = new ByteWriter();
        w.WriteU32(0);
        w.WriteU32(Magic);
        w.WriteU32(Version);
        w.WriteU32(SectorObjectSize + NodeObjectSize * (uint)sector.Nodes.Length);
        w.WriteU16(sector.SectorWord);
        w.WriteU16(sector.SectorId);
        w.WriteF32Array([sector.MinX, sector.MinY, sector.MaxX, sector.MaxY, sector.WaterLevel]);
        w.WriteU32((uint)sector.BuildId);
        w.WriteU32((uint)(sector.BuildId >> 32));
        w.WriteU16(sector.Unknown30);

        w.WriteU32((uint)sector.Links.Length);
        w.WriteU32Array(sector.Links);

        w.WriteU32((uint)sector.Nodes.Length);
        for (int i = 0; i < sector.Nodes.Length; i++)
        {
            NvmNode n = sector.Nodes[i];
            w.WriteU32(NodeObjectSize);
            w.WriteU16(sector.SectorId);
            w.WriteU16((ushort)i);
            w.WriteU16(n.GraphId);
            w.WriteU8(n.LinkCount);
            w.WriteU32(n.LinkOffset);
            WritePacked(w, n.Centroid);
            w.WriteU8((byte)n.NormalX);
            w.WriteU8((byte)n.NormalY);
            w.WriteU8((byte)n.NormalZ);
            w.WriteU8(n.UnderWater ? (byte)1 : (byte)0);
            w.WriteU8(n.AboveTerrain ? (byte)1 : (byte)0);
            w.WriteU32Array([n.A, n.B, n.C]);
            NvmClearance clearance = n.Clearance;
            w.WriteRaw(clearance);
        }

        WritePackedArray(w, sector.Vertices);
        w.WriteU32((uint)sector.Covers.Length);
        foreach (NvmCover cover in sector.Covers)
        {
            WriteCover(w, cover);
        }
        w.WriteU32((uint)sector.DynamicCovers.Length);
        foreach (NvmDynamicCover cover in sector.DynamicCovers)
        {
            WriteCover(w, cover.Cover);
            w.WriteU32(cover.First);
            w.WriteU32(cover.Second);
        }
        WritePackedArray(w, sector.CoverVertices);

        w.WriteU32((uint)sector.Quads.Length);
        foreach (NvmQuad q in sector.Quads)
        {
            w.WriteU32(q.Children is null ? 0u : 1u);
            w.WriteF32Array([q.MinX, q.MaxX, q.MinY, q.MaxY]);
            w.WriteU16(q.Count);
            w.WriteU16(q.Start);
            if (q.Children is { } children)
            {
                w.WriteU16Array(children);
            }
        }
        w.WriteU32((uint)sector.QuadNodes.Length);
        w.WriteU16Array(sector.QuadNodes);
        return w.ToArray();
    }

    /// <summary>
    /// The spatial index <c>CNavMeshQTreeWriter::Build</c> bakes: quads split down to 4 m leaves, each
    /// node filed in every leaf its corners' box touches, and empty leaves cut from their parent.
    /// </summary>
    /// <param name="triangles">Each node's three corners, in node order.</param>
    public static (NvmQuad[] Quads, ushort[] QuadNodes) BuildQuadTree(
        float minX, float minY, float maxX, float maxY, IReadOnlyList<(Vector2 A, Vector2 B, Vector2 C)> triangles)
    {
        var bounds = new List<(float MinX, float MaxX, float MinY, float MaxY)> { (minX, maxX, minY, maxY) };
        var children = new List<ushort[]?> { null };
        var parents = new List<int> { -1 };
        Split(0);

        void Split(int quad)
        {
            (float x0, float x1, float y0, float y1) = bounds[quad];
            if ((x1 - x0) * 0.5f < MinQuadHalfWidth)
            {
                return;
            }
            float mx = (x1 + x0) * 0.5f, my = (y1 + y0) * 0.5f;
            ushort first = (ushort)bounds.Count;
            bounds.AddRange([(mx, x1, my, y1), (x0, mx, my, y1), (mx, x1, y0, my), (x0, mx, y0, my)]);
            children.AddRange([null, null, null, null]);
            parents.AddRange([quad, quad, quad, quad]);
            children[quad] = [first, (ushort)(first + 1), (ushort)(first + 2), (ushort)(first + 3)];
            for (int i = 0; i < 4; i++)
            {
                Split(first + i);
            }
        }

        var members = new List<ushort>[bounds.Count];
        for (int i = 0; i < members.Length; i++)
        {
            members[i] = [];
        }
        for (int n = 0; n < triangles.Count; n++)
        {
            Insert(0, n);
        }

        void Insert(int quad, int node)
        {
            if (!Touches(bounds[quad], triangles[node]))
            {
                return;
            }
            if (children[quad] is { } split)
            {
                foreach (ushort child in split)
                {
                    Insert(child, node);
                }
            }
            else
            {
                members[quad].Add((ushort)node);
            }
        }

        var quads = new NvmQuad[bounds.Count];
        var quadNodes = new List<ushort>();
        for (int q = 0; q < quads.Length; q++)
        {
            if (q != 0 && children[q] is null && members[q].Count == 0)
            {
                ushort[] siblings = children[parents[q]]!;
                siblings[Array.IndexOf(siblings, (ushort)q)] = 0xFFFF;
            }
            (float x0, float x1, float y0, float y1) = bounds[q];
            quads[q] = new NvmQuad(x0, x1, y0, y1, (ushort)members[q].Count, (ushort)quadNodes.Count, children[q]);
            quadNodes.AddRange(members[q]);
        }
        return (quads, [.. quadNodes]);
    }

    /// <summary><c>QuadWriter::Contains</c>: a corner inside, or a box that does not miss.</summary>
    private static bool Touches((float MinX, float MaxX, float MinY, float MaxY) q, (Vector2 A, Vector2 B, Vector2 C) t)
    {
        bool Inside(Vector2 p) => p.X >= q.MinX && p.X <= q.MaxX && p.Y >= q.MinY && p.Y <= q.MaxY;
        if (Inside(t.A) || Inside(t.B) || Inside(t.C))
        {
            return true;
        }
        return !((t.A.X > q.MaxX && t.B.X > q.MaxX && t.C.X > q.MaxX)
                 || (t.A.X < q.MinX && t.B.X < q.MinX && t.C.X < q.MinX)
                 || (t.A.Y < q.MinY && t.B.Y < q.MinY && t.C.Y < q.MinY)
                 || (t.A.Y > q.MaxY && t.B.Y > q.MaxY && t.C.Y > q.MaxY));
    }

    private static PackedVector ReadPacked(ref ByteCursor c) => new(c.ReadI16(), c.ReadI16(), c.ReadI16());

    private static PackedVector[] ReadPackedArray(ref ByteCursor c)
    {
        var vectors = new PackedVector[c.ReadU32()];
        for (int i = 0; i < vectors.Length; i++)
        {
            vectors[i] = ReadPacked(ref c);
        }
        return vectors;
    }

    private static NvmCover ReadCover(ref ByteCursor c) => new(c.ReadU8(), c.ReadU16(), c.ReadU8(), c.ReadU16());

    private static void WritePacked(ByteWriter w, PackedVector p) => w.WriteI16Array([p.X, p.Y, p.Z]);

    private static void WritePackedArray(ByteWriter w, PackedVector[] vectors)
    {
        w.WriteU32((uint)vectors.Length);
        foreach (PackedVector p in vectors)
        {
            WritePacked(w, p);
        }
    }

    private static void WriteCover(ByteWriter w, NvmCover cover)
    {
        w.WriteU8(cover.Height);
        w.WriteU16(cover.Vertex);
        w.WriteU8(cover.Type);
        w.WriteU16(cover.Node);
    }
}
