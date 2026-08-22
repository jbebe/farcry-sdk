using System.Numerics;
using System.Text.RegularExpressions;

namespace JackAll.Tools.Xbg;

/// <summary>One drawable triangle list: one (LOD, part, material) slice of an .xbg, sharing a single
/// vertex buffer with its sibling primitives on the same part.</summary>
public sealed class XbgSubmesh
{
    public required int LodLevel { get; init; }
    public required int MaterialIndex { get; init; }
    public required string MaterialName { get; init; }

    /// <summary>The named part this belongs to - a wheel, a bumper, a wall - without its LOD
    /// suffix. Empty when the file names no part for it.</summary>
    public string PartName { get; init; } = "";

    /// <summary>What moves this part's vertices into model space: the world matrix of the bone they
    /// are stored relative to - the part's own bone when rigid, the skeleton root when skinned. Null
    /// when that bone is the identity and the vertices already sit in model space.</summary>
    public Matrix4x4? PartTransform { get; init; }

    public required Vector3[] Positions { get; init; }
    public Vector3[]? Normals { get; init; }

    /// <summary>A position from <see cref="Positions"/> moved out of pivot space into the model's.
    /// Positions stay unplaced because parts sharing a vertex buffer need different placements, so
    /// every consumer of the geometry goes through this.</summary>
    public Vector3 Place(Vector3 position)
        => PartTransform is { } m ? Vector3.Transform(position, m) : position;

    /// <summary>The placement is rigid, so a normal only needs its rotation.</summary>
    public Vector3 PlaceNormal(Vector3 normal)
        => PartTransform is { } m ? Vector3.TransformNormal(normal, m) : normal;

    /// <summary>UV channel 0 in the game's D3D space (V=0 is the top row); null when the file has
    /// none.</summary>
    public Vector2[]? Uvs { get; init; }

    /// <summary>UV channel 1, the second set a material can bind a texture to through the Group
    /// half of its tiling vector. 99% of retail meshes carry one.</summary>
    public Vector2[]? Uvs1 { get; init; }

    /// <summary>The per-vertex colour the engine calls the vertex mask: its blue channel blends the
    /// material's two diffuse tints, red the speculars, alpha is occlusion. Null when the file
    /// carries none, which the engine treats as white.</summary>
    public Vector4[]? Colours { get; init; }

    /// <summary>Triangle list, indices local to <see cref="Positions"/>.</summary>
    public required int[] Indices { get; init; }
}

/// <summary>
/// A mesh flattened for drawing: vertex positions, normals, UVs and triangle lists per
/// (LOD, part, material), enough to render the model.
/// </summary>
/// <remarks>
/// A projection over <see cref="XbgFile"/>, which owns the format. Keeping it separate is what lets
/// the viewer, the world baker, the RealTree converter and the CLI exporter share one shape without
/// each of them walking chunks.
/// <para>
/// It previously carried its own parser, ported from the community Blender importer, which read
/// 12-byte chunk headers, skipped DIKS as 4-byte entries and placed rigid parts by matching a
/// part's name against a node's. That last one disagrees on 291 shipped parts and is wrong on all
/// of them - some meshes have a node whose own name ends in <c>_LOD0</c> - so this now takes the
/// placement DIKS names outright.
/// </para>
/// </remarks>
public sealed partial class XbgModel
{
    [GeneratedRegex(@"_LOD\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex LodSuffixRegex();

    public required IReadOnlyList<string> Materials { get; init; }
    public required IReadOnlyList<XbgSubmesh> Submeshes { get; init; }
    public required IReadOnlyList<int> LodLevels { get; init; }

    public static XbgModel Parse(byte[] data)
    {
        XbgFile file = XbgFile.Parse(data);
        Matrix4x4[] world = file.NodeWorldMatrices();
        float uvTranslate = file.UvCompress.Length > 0 ? file.UvCompress[0] : 0.0f;
        float uvScale = file.UvCompress.Length > 1 ? file.UvCompress[1] : 1.0f;

        var submeshes = new List<XbgSubmesh>();
        for (int lodLevel = 0; lodLevel < file.Lods.Count; lodLevel++)
        {
            foreach (ClusterGeometry geometry in XbgGeometry.ReadLod(file, file.Lods[lodLevel]))
            {
                XbgPart part = file.Parts[(int)geometry.Part];
                XbgCluster cluster = part.Clusters[(int)geometry.Cluster];
                Matrix4x4? placement = file.PartPlacement(part.Name, world);
                submeshes.Add(new XbgSubmesh
                {
                    LodLevel = lodLevel,
                    PartName = LodSuffixRegex().Replace(part.Name, ""),
                    PartTransform = placement is { IsIdentity: false } ? placement : null,
                    MaterialIndex = cluster.MaterialIndex,
                    MaterialName = cluster.MaterialIndex < file.Materials.Count
                        ? file.Materials[cluster.MaterialIndex]
                        : "",
                    Positions = Points(geometry.Vertices.Positions(file.PosScale)),
                    Normals = Directions(geometry.Vertices.Normals()),
                    Uvs = Coordinates(geometry.Vertices.Uvs(uvTranslate, uvScale, 0)),
                    Uvs1 = Coordinates(geometry.Vertices.Uvs(uvTranslate, uvScale, 1)),
                    Colours = Colours(geometry.Vertices.Colours()),
                    Indices = [.. geometry.Indices],
                });
            }
        }

        return new XbgModel
        {
            Materials = file.Materials,
            Submeshes = submeshes,
            LodLevels = [.. submeshes.Select(s => s.LodLevel).Distinct().Order()],
        };
    }

    public static (Vector3 Min, Vector3 Max) Bounds(IEnumerable<XbgSubmesh> submeshes)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (XbgSubmesh submesh in submeshes)
        {
            foreach (int i in submesh.Indices)
            {
                Vector3 placed = submesh.Place(submesh.Positions[i]);
                min = Vector3.Min(min, placed);
                max = Vector3.Max(max, placed);
            }
        }

        return min.X > max.X ? (Vector3.Zero, Vector3.Zero) : (min, max);
    }

    /// <summary>Fallback for files with no NORMAL vertex component: accumulate each triangle's face
    /// normal into its three vertices and normalise, so shading still reads as a solid rather than
    /// flat per-face facets.</summary>
    public static Vector3[] ComputeSmoothNormals(Vector3[] positions, int[] indices)
    {
        var normals = new Vector3[positions.Length];
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            Vector3 faceNormal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += faceNormal;
            normals[b] += faceNormal;
            normals[c] += faceNormal;
        }

        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i] == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(normals[i]);
        }

        return normals;
    }

    private static Vector3[] Points((float X, float Y, float Z)[] values)
        => [.. values.Select(v => new Vector3(v.X, v.Y, v.Z))];

    private static Vector3[]? Directions((float X, float Y, float Z)[]? values)
        => values is null ? null : Points(values);

    private static Vector2[]? Coordinates((float U, float V)[]? values)
        => values is null ? null : [.. values.Select(v => new Vector2(v.U, v.V))];

    private static Vector4[]? Colours((float R, float G, float B, float A)[]? values)
        => values is null ? null : [.. values.Select(v => new Vector4(v.R, v.G, v.B, v.A))];
}
