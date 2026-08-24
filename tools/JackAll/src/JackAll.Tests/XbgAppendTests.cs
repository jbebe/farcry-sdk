using JackAll.Tools.Fc2Model;
using JackAll.Tools.Xbg;

namespace JackAll.Tests;

/// <summary>
/// The append gate: give every shipped mesh a part it never had, and require the file back with all
/// of its own content intact.
/// </summary>
/// <remarks>
/// <see cref="XbgAuthorTests"/> proves a mesh can be rebuilt from decoded content. This proves the
/// thing a modeller actually needs - that a part from no file at all can be added to one - and that
/// doing so disturbs nothing already there.
/// <para>
/// It appends through <see cref="MeshDocument"/> rather than onto an <see cref="XbgFile"/>, because
/// that is the road a part added in Blender travels: the add-on writes the document and
/// <see cref="MeshDocument.ToXbg"/> builds the container from it.
/// </para>
/// </remarks>
public sealed class XbgAppendTests
{
    private const string Added = "JACKALL_APPEND_LOD0";

    /// <summary>Vertices in the appended triangle.</summary>
    private const int TriangleVertices = 3;

    [Fact]
    public void Every_shipped_mesh_takes_a_new_part()
    {
        List<string> failures = [];
        int seen = 0;

        foreach (string path in Fc2Corpus.Find(".xbg"))
        {
            seen++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                XbgFile after = XbgFile.Parse(
                    WithExtraPart(MeshDocument.From(XbgFile.Parse(original))));
                string? complaint = Intact(XbgFile.Parse(original), after);
                if (complaint is not null)
                {
                    failures.Add($"{Path.GetFileName(path)}: {complaint}");
                }
            }
            catch (Exception error)
            {
                failures.Add($"{Path.GetFileName(path)}: {error.Message}");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{seen - failures.Count}/{seen} meshes took a new part. First failures:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, failures.Take(5)));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".xbg").Any(), Fc2Corpus.MissingMessage(".xbg"));

    /// <summary>
    /// The document with one more part in its first LOD, drawing a triangle lifted from a part
    /// already there so every channel holds what that part's vertex format declares.
    /// </summary>
    private static byte[] WithExtraPart(MeshDocument document)
    {
        MeshLod lod = document.Lods[0];
        MeshGeometry donor =
            lod.Geometry.FirstOrDefault(geometry => geometry.VertexCount >= TriangleVertices)
            ?? throw new InvalidDataException("no cluster here holds a triangle to copy a format from.");
        MeshPart donorPart = document.Parts[(int)donor.Part];
        MeshCluster template = donorPart.Clusters[(int)donor.Cluster];

        document.Parts.Add(new MeshPart
        {
            Name = Added,
            LodMetric = donorPart.LodMetric,
            Bounds = [.. donorPart.Bounds],
            PlacementNode = XbgFile.NoPlacement,
            Clusters =
            {
                new MeshCluster
                {
                    MaterialIndex = template.MaterialIndex,
                    Flags = template.Flags,
                    Palette = [.. template.Palette],
                },
            },
        });

        lod.Geometry.Add(new MeshGeometry
        {
            Buffer = donor.Buffer,
            Part = (uint)(document.Parts.Count - 1),
            Cluster = 0,
            VertexCount = TriangleVertices,
            Positions = Head(donor.Positions, 3)!,
            Uvs = Head(donor.Uvs, 2),
            Uvs1 = Head(donor.Uvs1, 2),
            Normals = Head(donor.Normals, 3),
            Tangents = Head(donor.Tangents, 3),
            Binormals = Head(donor.Binormals, 3),
            Colours = Head(donor.Colours, 4),
            SkinWeights = Head(donor.SkinWeights, MeshGeometry.SkinStride),
            SkinSlots = Head(donor.SkinSlots, MeshGeometry.SkinStride),
            Indices = [0, 1, 2],
        });

        return document.ToXbg().Write();
    }

    /// <summary>The first triangle's worth of one component, or null when it carries none.</summary>
    private static T[]? Head<T>(T[]? values, int width)
        => values is null ? null : values[..(TriangleVertices * width)];

    /// <summary>What the edit disturbed that it should not have, or null when nothing did.</summary>
    private static string? Intact(XbgFile before, XbgFile after)
    {
        if (after.Parts.Count != before.Parts.Count + 1)
        {
            return $"{after.Parts.Count} parts, expected {before.Parts.Count + 1}";
        }
        if (after.PartRefs.Count != after.Parts.Count)
        {
            return $"{after.PartRefs.Count} placement entries for {after.Parts.Count} parts";
        }
        if (after.Nodes.Count != before.Nodes.Count)
        {
            return $"{after.Nodes.Count} nodes, expected {before.Nodes.Count}";
        }
        if (!after.Materials.SequenceEqual(before.Materials))
        {
            return "the material list moved";
        }

        for (int index = 0; index < before.Parts.Count; index++)
        {
            XbgPart was = before.Parts[index];
            XbgPart now = after.Parts[index];
            if (now.Name != was.Name)
            {
                return $"part {index} is now {now.Name}, was {was.Name}";
            }
            if (now.Clusters.Count != was.Clusters.Count)
            {
                return $"{was.Name} has {now.Clusters.Count} clusters, had {was.Clusters.Count}";
            }
            for (int slot = 0; slot < was.Clusters.Count; slot++)
            {
                if (now.Clusters[slot].VertexCount != was.Clusters[slot].VertexCount
                    || now.Clusters[slot].FaceCount != was.Clusters[slot].FaceCount
                    || now.Clusters[slot].MaterialIndex != was.Clusters[slot].MaterialIndex)
                {
                    return $"{was.Name} cluster {slot} was repartitioned";
                }
            }
        }

        XbgPart fresh = after.Parts[^1];
        if (fresh.Name != Added)
        {
            return $"the appended part came back as {fresh.Name}";
        }
        if (fresh.Lod != 0)
        {
            return $"the appended part landed at LOD tier {fresh.Lod}";
        }
        if (fresh.Clusters.Count != 1
            || fresh.Clusters[0].FaceCount != 1
            || fresh.Clusters[0].VertexCount != TriangleVertices)
        {
            return "the appended part did not come back as one triangle";
        }
        return VerticesHeld(before, after);
    }

    /// <summary>Whether every original cluster's vertices survived the relayout unchanged.</summary>
    private static string? VerticesHeld(XbgFile before, XbgFile after)
    {
        List<ClusterGeometry> was = XbgGeometry.ReadLod(before, before.Lods[0]);
        List<ClusterGeometry> now = XbgGeometry.ReadLod(after, after.Lods[0]);
        for (int index = 0; index < was.Count; index++)
        {
            if (!now[index].Vertices.Pack().AsSpan().SequenceEqual(was[index].Vertices.Pack()))
            {
                return $"submesh {index} came back with different vertex bytes";
            }
            if (!now[index].Indices.SequenceEqual(was[index].Indices))
            {
                return $"submesh {index} came back with different triangles";
            }
        }
        return null;
    }
}
