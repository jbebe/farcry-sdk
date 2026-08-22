using System.Buffers.Binary;

namespace JackAll.Tools.Xbg;

/// <summary>One drawable block's own vertices and triangles, indices local to it.</summary>
public sealed class ClusterGeometry
{
    public required int Submesh { get; init; }

    public required uint Buffer { get; init; }

    public required uint Part { get; init; }

    public required uint Cluster { get; init; }

    public required VertexStream Vertices { get; set; }

    public List<int> Indices { get; set; } = [];

    public int FaceCount => Indices.Count / 3;
}

/// <summary>
/// Splits a LOD into its clusters' geometry, and puts it back together.
/// </summary>
/// <remarks>
/// A LOD stores one flat vertex block and one flat index block. Every cluster owns a contiguous run
/// of each: its vertices are <c>VertexCount</c> of them at the running total for that buffer, its
/// indices are <c>FaceCount * 3</c> at the running total for the LOD, and both are ordered by
/// submesh. Measured across the retail set, 29,296 of 29,296 clusters and 9,746 of 9,746 LODs, with
/// nothing left over.
/// <para>
/// So editing a part means rebuilding the blocks rather than patching them, and every offset and
/// count in the LOD is derived here rather than tracked.
/// </para>
/// </remarks>
public static class XbgGeometry
{
    public static List<ClusterGeometry> ReadLod(XbgFile model, XbgLod lod)
    {
        ushort[] indices = UnpackIndices(lod);
        Dictionary<uint, VertexStream> streams = [];
        Dictionary<uint, int> baseVertex = [];
        List<ClusterGeometry> out_ = [];

        for (int position = 0; position < lod.Submeshes.Count; position++)
        {
            XbgSubmeshRef submesh = lod.Submeshes[position];
            XbgCluster cluster = ClusterOf(model, submesh, position);
            if (!streams.TryGetValue(submesh.Buffer, out VertexStream? stream))
            {
                XbgVertexBuffer buffer = lod.VertexBuffers[(int)submesh.Buffer];
                stream = VertexStream.Unpack(lod.VertexData, buffer, (int)buffer.VertexCount);
                streams[submesh.Buffer] = stream;
            }

            int start = baseVertex.GetValueOrDefault(submesh.Buffer);
            baseVertex[submesh.Buffer] = start + cluster.VertexCount;

            List<int> run = [];
            for (int i = 0; i < cluster.FaceCount * 3; i++)
            {
                run.Add(indices[submesh.IndexOffset + i] - start);
            }

            out_.Add(new ClusterGeometry
            {
                Submesh = position,
                Buffer = submesh.Buffer,
                Part = submesh.Part,
                Cluster = submesh.Cluster,
                Vertices = stream.Slice(start, cluster.VertexCount),
                Indices = run,
            });
        }
        return out_;
    }

    public static void WriteLod(XbgFile model, XbgLod lod, List<ClusterGeometry> geometries)
    {
        if (geometries.Count != lod.Submeshes.Count)
        {
            throw new InvalidDataException(
                $"{geometries.Count} geometries for {lod.Submeshes.Count} submeshes.");
        }

        List<byte> vertexData = [];
        for (int index = 0; index < lod.VertexBuffers.Count; index++)
        {
            lod.VertexBuffers[index].Offset = (uint)vertexData.Count;
            foreach (ClusterGeometry geometry in geometries.Where(g => g.Buffer == index))
            {
                vertexData.AddRange(geometry.Vertices.Pack());
            }
        }
        lod.VertexData = [.. vertexData];

        List<int> indices = [];
        Dictionary<uint, int> baseVertex = [];
        for (int position = 0; position < geometries.Count; position++)
        {
            XbgSubmeshRef submesh = lod.Submeshes[position];
            ClusterGeometry geometry = geometries[position];
            XbgCluster cluster = ClusterOf(model, submesh, position);
            XbgVertexBuffer buffer = lod.VertexBuffers[(int)geometry.Buffer];

            int start = baseVertex.GetValueOrDefault(geometry.Buffer);
            baseVertex[geometry.Buffer] = start + geometry.Vertices.Count;

            submesh.IndexOffset = (uint)indices.Count;
            indices.AddRange(geometry.Indices.Select(index => index + start));
            cluster.FaceCount = (ushort)geometry.FaceCount;
            cluster.VertexCount = (ushort)geometry.Vertices.Count;
            cluster.Stride = (ushort)buffer.Stride;
            // The submesh's last vertex index, then its byte offset into the LOD's whole
            // vertex block rather than into its own buffer.
            submesh.Trailing =
            [
                (uint)(start + cluster.VertexCount - 1),
                buffer.Offset + (uint)(start * buffer.Stride),
                0,
            ];
        }

        foreach ((XbgVertexBuffer buffer, int index) in lod.VertexBuffers.Select((b, i) => (b, i)))
        {
            buffer.VertexCount = (uint)baseVertex.GetValueOrDefault((uint)index);
        }
        lod.IndexData = PackIndices(indices);
    }

    public static ushort[] UnpackIndices(XbgLod lod)
    {
        var indices = new ushort[lod.IndexData.Length / 2];
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = BinaryPrimitives.ReadUInt16LittleEndian(lod.IndexData.AsSpan(i * 2));
        }
        return indices;
    }

    public static byte[] PackIndices(IReadOnlyList<int> indices)
    {
        var packed = new byte[indices.Count * 2];
        for (int i = 0; i < indices.Count; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(packed.AsSpan(i * 2), (ushort)indices[i]);
        }
        return packed;
    }

    private static XbgCluster ClusterOf(XbgFile model, XbgSubmeshRef submesh, int position)
    {
        if (submesh.Part >= model.Parts.Count)
        {
            throw new InvalidDataException(
                $"Submesh {position} names part {submesh.Part} of {model.Parts.Count}.");
        }
        List<XbgCluster> clusters = model.Parts[(int)submesh.Part].Clusters;
        if (submesh.Cluster >= clusters.Count)
        {
            throw new InvalidDataException(
                $"Submesh {position} names cluster {submesh.Cluster} of {clusters.Count}.");
        }
        return clusters[(int)submesh.Cluster];
    }
}
