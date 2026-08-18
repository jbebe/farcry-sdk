using System.Collections.Concurrent;
using System.Numerics;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Xbg;
using JackAll.Tools.Xbm;

namespace JackAll.Tools.World;

/// <summary>A contiguous slice of a baked index list.</summary>
public readonly record struct IndexRange(int Start, int Count);

/// <summary>The slice of a fine tier drawn with one material, with the diffuse .xbt it binds when
/// the material name resolved to one.</summary>
public sealed record MaterialRange(int Start, int Count, string MaterialName, string? DiffuseTexturePath = null);

/// <summary>
/// One unique .xbg a world references, baked into GPU-ready arrays: interleaved
/// [px py pz nx ny nz u v] vertices and a triangle index list holding two detail tiers.
/// </summary>
public sealed class WorldModel
{
    public const int FloatsPerVertex = 8;

    public required string Path { get; init; }
    public required float[] Vertices { get; init; }
    public required int[] Indices { get; init; }

    /// <summary>The finest LOD within the triangle budget - what renders near the camera.</summary>
    public required IndexRange Fine { get; init; }

    /// <summary>The file's coarsest LOD - what renders in the surrounding sector ring.</summary>
    public required IndexRange Coarse { get; init; }

    /// <summary>Material sub-ranges of <see cref="Fine"/>, for textured drawing.</summary>
    public required IReadOnlyList<MaterialRange> MaterialRanges { get; init; }

    public int VertexCount => Vertices.Length / FloatsPerVertex;
}

/// <summary>Every entity resolved to renderable geometry, plus what the status line reports.</summary>
public sealed class WorldModelSet
{
    public required IReadOnlyList<WorldModel> Models { get; init; }

    /// <summary>Index into <see cref="Models"/> per entity; an entity absent here keeps its
    /// billboard marker.</summary>
    public required IReadOnlyDictionary<WorldEntity, int> ModelIndexByEntity { get; init; }

    /// <summary>Referenced paths the VFS missed or the parser could not turn into triangles.</summary>
    public required int FailedPathCount { get; init; }
}

/// <summary>
/// Resolves each placed entity to the .xbg it renders with and bakes every referenced mesh, so the
/// map can draw entities as their real models. Resolution mirrors the engine: the entity's own
/// graphics component wins, the archetype's is the fallback.
/// </summary>
public static class WorldModels
{
    /// <summary>Fine-tier ceiling per mesh; the finest LOD at or under it wins.</summary>
    public const int FineTriangleBudget = 10_000;

    /// <summary>An FC2 sector is 64 m on a side.</summary>
    public const float SectorMeters = 64f;

    /// <summary>The detail tiers as Chebyshev distances in sectors from the camera's: fine within
    /// <see cref="FineRadius"/>, coarse within <see cref="CoarseRadius"/>, marker beyond.</summary>
    public const int FineRadius = 1;
    public const int CoarseRadius = 4;

    /// <summary>The .xbg path a node's graphics component names, from either shape the data ships
    /// in: worldsector files carry the slot fields flat on the component, entity libraries nest
    /// them in per-slot "object" children.</summary>
    public static string? MeshPath(FcbObject entityNode)
    {
        if (FcbEntityFields.FindComponent(entityNode, WorldHashes.CGraphicComponent) is not { } component)
        {
            return null;
        }

        string direct = FcbEntityFields.ReadString(component, WorldHashes.TextObjModel);
        if (direct.Length > 0)
        {
            return NameHash.Normalize(direct);
        }

        foreach (FcbObject slot in component.Children)
        {
            if (slot.TypeHash != WorldHashes.GraphicObject)
            {
                continue;
            }

            string path = FcbEntityFields.ReadString(slot, WorldHashes.TextObjModel);
            if (path.Length > 0)
            {
                return NameHash.Normalize(path);
            }
        }

        return null;
    }

    public static WorldModelSet Load(
        IReadOnlyList<WorldEntity> entities, ArchetypeIndex archetypes,
        Func<string, byte[]?> readByPath, IProgress<string>? progress = null)
    {
        Func<string, string?> diffuseByMaterial = DiffuseResolver(readByPath);

        // A few hundred archetypes cover ~90k entities, so the fallback walk runs once per name.
        var archetypeMeshPath = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var pathByEntity = new Dictionary<WorldEntity, string>();
        foreach (WorldEntity entity in entities)
        {
            if (entity.Position is null)
            {
                continue;
            }

            string? path = MeshPath(entity.Node);
            if (path is null && entity.ArchetypeName.Length > 0)
            {
                if (!archetypeMeshPath.TryGetValue(entity.ArchetypeName, out path))
                {
                    path = archetypes.Winner(entity.ArchetypeName) is { } winner ? MeshPath(winner.Node) : null;
                    archetypeMeshPath[entity.ArchetypeName] = path;
                }
            }

            if (path is not null)
            {
                pathByEntity[entity] = path;
            }
        }

        List<string> unique = [.. pathByEntity.Values.Distinct(StringComparer.OrdinalIgnoreCase)];
        var baked = new ConcurrentDictionary<string, WorldModel?>(StringComparer.OrdinalIgnoreCase);
        int done = 0;
        Parallel.ForEach(unique, path =>
        {
            WorldModel? model = null;
            if (readByPath(path) is { } bytes)
            {
                // A single corrupt or unexpected file must not take down the world load.
                try
                {
                    model = Bake(path, XbgModel.Parse(bytes), FineTriangleBudget, diffuseByMaterial);
                }
                catch (Exception)
                {
                }
            }

            baked[path] = model;
            int soFar = Interlocked.Increment(ref done);
            if (soFar % 200 == 0)
            {
                progress?.Report($"Loading models: {soFar:N0}/{unique.Count:N0}");
            }
        });

        var models = new List<WorldModel>();
        var indexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in unique)
        {
            if (baked[path] is { } model)
            {
                indexByPath[path] = models.Count;
                models.Add(model);
            }
        }

        var modelIndexByEntity = new Dictionary<WorldEntity, int>(pathByEntity.Count);
        foreach ((WorldEntity entity, string path) in pathByEntity)
        {
            if (indexByPath.TryGetValue(path, out int index))
            {
                modelIndexByEntity[entity] = index;
            }
        }

        progress?.Report(
            $"Baked {models.Count:N0} of {unique.Count:N0} meshes for {modelIndexByEntity.Count:N0} entities");
        return new WorldModelSet
        {
            Models = models,
            ModelIndexByEntity = modelIndexByEntity,
            FailedPathCount = unique.Count - models.Count,
        };
    }

    /// <summary>Null when the parse yielded no drawable triangles (DNKS mismatch or empty mesh).</summary>
    public static WorldModel? Bake(
        string path, XbgModel model, int fineTriangleBudget, Func<string, string?>? diffuseByMaterial = null)
    {
        var trianglesPerLod = new Dictionary<int, int>();
        foreach (XbgSubmesh submesh in model.Submeshes)
        {
            trianglesPerLod[submesh.LodLevel] =
                trianglesPerLod.GetValueOrDefault(submesh.LodLevel) + submesh.Indices.Length / 3;
        }

        List<int> drawable = [.. trianglesPerLod.Where(p => p.Value > 0).Select(p => p.Key)];
        if (drawable.Count == 0)
        {
            return null;
        }

        int coarseLod = drawable.MinBy(l => (trianglesPerLod[l], -l));
        List<int> fitting = [.. drawable.Where(l => trianglesPerLod[l] <= fineTriangleBudget)];
        int fineLod = fitting.Count > 0 ? fitting.MaxBy(l => (trianglesPerLod[l], -l)) : coarseLod;

        List<XbgSubmesh> fineSubmeshes = SubmeshesAt(model, fineLod);
        List<XbgSubmesh> coarseSubmeshes = coarseLod == fineLod ? [] : SubmeshesAt(model, coarseLod);

        // Sized exactly: distinct position blocks for the vertex side, every index for the other.
        var blockRefs = new HashSet<object>(ReferenceEqualityComparer.Instance);
        int vertexFloats = 0, indexCount = 0;
        foreach (XbgSubmesh submesh in fineSubmeshes.Concat(coarseSubmeshes))
        {
            if (blockRefs.Add(submesh.Positions))
            {
                vertexFloats += submesh.Positions.Length * WorldModel.FloatsPerVertex;
            }
            indexCount += submesh.Indices.Length;
        }

        var vertices = new List<float>(vertexFloats);
        var indices = new List<int>(indexCount);
        var materialRanges = new List<MaterialRange>();
        IndexRange fine = BakeLod(fineSubmeshes, vertices, indices, materialRanges, diffuseByMaterial);
        IndexRange coarse = coarseLod == fineLod
            ? fine
            : BakeLod(coarseSubmeshes, vertices, indices, null, null);

        return new WorldModel
        {
            Path = path,
            Vertices = [.. vertices],
            Indices = [.. indices],
            Fine = fine,
            Coarse = coarse,
            MaterialRanges = materialRanges,
        };
    }

    private static List<XbgSubmesh> SubmeshesAt(XbgModel model, int lod)
        => [.. model.Submeshes.Where(s => s.LodLevel == lod && s.Indices.Length > 0)];

    private static IndexRange BakeLod(
        List<XbgSubmesh> submeshes, List<float> vertices, List<int> indices,
        List<MaterialRange>? materialRanges, Func<string, string?>? diffuseByMaterial)
    {
        int rangeStart = indices.Count;

        // Submeshes on one part share a Positions array by reference; append each block once.
        var blockBase = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        foreach (IGrouping<object, XbgSubmesh> block in
            submeshes.GroupBy(s => (object)s.Positions, ReferenceEqualityComparer.Instance))
        {
            XbgSubmesh first = block.First();
            blockBase[first.Positions] = vertices.Count / WorldModel.FloatsPerVertex;
            Vector3[] normals = first.Normals ?? XbgModel.ComputeSmoothNormals(
                first.Positions, [.. block.SelectMany(s => s.Indices)]);
            for (int i = 0; i < first.Positions.Length; i++)
            {
                Vector3 p = first.Positions[i];
                Vector3 n = normals[i];
                Vector2 uv = first.Uvs is { } uvs ? uvs[i] : Vector2.Zero;
                vertices.Add(p.X);
                vertices.Add(p.Y);
                vertices.Add(p.Z);
                vertices.Add(n.X);
                vertices.Add(n.Y);
                vertices.Add(n.Z);
                vertices.Add(uv.X);
                vertices.Add(uv.Y);
            }
        }

        // Indices grouped by material, so each material's triangles form one contiguous range.
        foreach (IGrouping<int, XbgSubmesh> byMaterial in submeshes.GroupBy(s => s.MaterialIndex).OrderBy(g => g.Key))
        {
            int materialStart = indices.Count;
            foreach (XbgSubmesh submesh in byMaterial)
            {
                int baseIndex = blockBase[submesh.Positions];
                foreach (int index in submesh.Indices)
                {
                    indices.Add(baseIndex + index);
                }
            }

            string materialName = byMaterial.First().MaterialName;
            materialRanges?.Add(new MaterialRange(
                materialStart, indices.Count - materialStart, materialName,
                diffuseByMaterial?.Invoke(materialName)));
        }

        return new IndexRange(rangeStart, indices.Count - rangeStart);
    }

    /// <summary>
    /// The per-material diffuse lookup the bake consumes. A mesh's material table stores the
    /// .xbm's archive path, so each referenced material is read directly and remembered -
    /// materials are shared across meshes.
    /// </summary>
    public static Func<string, string?> DiffuseResolver(Func<string, byte[]?> readByPath)
    {
        var cache = new ConcurrentDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        return materialPath => !materialPath.EndsWith(".xbm", StringComparison.OrdinalIgnoreCase)
            ? null
            : cache.GetOrAdd(materialPath, path =>
            {
                if (readByPath(path) is not { } bytes || bytes.Length < 4 ||
                    bytes[0] != (byte)'H' || bytes[1] != (byte)'S' || bytes[2] != (byte)'E' || bytes[3] != (byte)'M')
                {
                    return null;
                }

                try
                {
                    return DiffuseTextureOf(XbmMaterial.Parse(bytes));
                }
                catch (Exception)
                {
                    return null;
                }
            });
    }

    /// <summary>The albedo across the material templates: Generic and Hair bind DiffuseTexture1,
    /// Skin puts it in SkinTexture, Cloth in FabricTexture.</summary>
    public static string? DiffuseTextureOf(XbmMaterial material)
    {
        string? Slot(string key) => material.Textures
            .FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;

        string? value = Slot("DiffuseTexture1") ?? Slot("SkinTexture") ?? Slot("FabricTexture")
            ?? material.Textures.FirstOrDefault(
                t => t.Key.StartsWith("DiffuseTexture", StringComparison.OrdinalIgnoreCase))?.Value;
        return value is { Length: > 0 } ? NameHash.Normalize(value) : null;
    }
}
