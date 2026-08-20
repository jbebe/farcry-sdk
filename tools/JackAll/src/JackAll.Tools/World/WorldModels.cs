using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Xbg;
using JackAll.Tools.Xbm;

namespace JackAll.Tools.World;

/// <summary>A contiguous slice of a baked index list.</summary>
public readonly record struct IndexRange(int Start, int Count);

/// <summary>How a material means its diffuse texture's alpha channel to be read. Most FC2 materials
/// are opaque and carry a gloss or spec mask in alpha, so reading it as coverage erases the surface.
/// </summary>
public enum MaterialAlpha
{
    Opaque,
    Mask,
    Blend,
}

/// <summary>
/// What one .xbm contributes to a draw. The engine's shader reads the diffuse map as a base and
/// takes the colour from the two tints, blended per vertex - so a material whose map is a neutral
/// grey renders grey until the tints are applied.
/// </summary>
public readonly record struct MaterialSurface(
    string? DiffuseTexturePath, MaterialAlpha Alpha, Vector3 TintBase, Vector3 Tint)
{
    /// <summary>What an unresolved material draws as: the texture unchanged.</summary>
    public static readonly MaterialSurface None =
        new(null, MaterialAlpha.Opaque, Vector3.One, Vector3.One);
}

/// <summary>The slice of a fine tier drawn with one material, with the diffuse .xbt it binds when
/// the material name resolved to one.</summary>
public sealed record MaterialRange(int Start, int Count, string MaterialName, MaterialSurface Surface)
{
    public string? DiffuseTexturePath => Surface.DiffuseTexturePath;
    public MaterialAlpha Alpha => Surface.Alpha;
}

/// <summary>
/// One unique .xbg a world references, baked into GPU-ready arrays: interleaved
/// [px py pz nx ny nz u v mask] vertices and a triangle index list holding two detail tiers.
/// </summary>
public sealed class WorldModel
{
    public const int FloatsPerVertex = 9;

    public required string Path { get; init; }
    public required float[] Vertices { get; init; }
    public required int[] Indices { get; init; }

    /// <summary>The finest LOD within the triangle budget - what renders near the camera.</summary>
    public required IndexRange Fine { get; init; }

    /// <summary>The file's coarsest LOD - what renders in the surrounding sector ring.</summary>
    public required IndexRange Coarse { get; init; }

    /// <summary>Material sub-ranges of <see cref="Fine"/>, for textured drawing.</summary>
    public required IReadOnlyList<MaterialRange> MaterialRanges { get; init; }

    /// <summary>Axis-aligned extent of the baked geometry in model space, which is what picking
    /// tests a click ray against.</summary>
    public required Vector3 LocalMin { get; init; }
    public required Vector3 LocalMax { get; init; }

    public int VertexCount => Vertices.Length / FloatsPerVertex;
}

/// <summary>Every entity resolved to renderable geometry, plus what the status line reports.</summary>
public sealed class WorldModelSet
{
    public required IReadOnlyList<WorldModel> Models { get; init; }

    /// <summary>Indices into <see cref="Models"/> per entity - one per graphics slot it filled. An
    /// entity absent here keeps its billboard marker.</summary>
    public required IReadOnlyDictionary<WorldEntity, int[]> ModelIndicesByEntity { get; init; }

    /// <summary>Referenced paths the VFS missed or the parser could not turn into triangles.</summary>
    public required int FailedPathCount { get; init; }

    /// <summary>Positioned entities naming no mesh anywhere, on themselves or their archetype.
    /// Mostly procedural vegetation and logic entities, which other layers draw - so a jump here is
    /// the signal that model resolution, rather than rendering, is losing something.</summary>
    public required int EntitiesWithoutMesh { get; init; }
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

    /// <summary>Every .xbg a node's graphics component names, from either shape the data ships in:
    /// worldsector files carry the slot fields flat on the component, entity libraries nest them in
    /// per-slot "object" children. A component holding several slots draws all of them - that is
    /// where a vehicle keeps its wheels and glass, and a building its separate wall pieces.</summary>
    public static IReadOnlyList<string> MeshPaths(FcbObject entityNode)
    {
        if (FcbEntityFields.FindComponent(entityNode, WorldHashes.CGraphicComponent) is not { } component)
        {
            return [];
        }

        var paths = new List<string>();
        void Take(string value)
        {
            if (value.Length == 0)
            {
                return;
            }

            string path = NameHash.Normalize(value);
            if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }

        Take(FcbEntityFields.ReadString(component, WorldHashes.TextObjModel));
        foreach (FcbObject slot in component.Children)
        {
            if (slot.TypeHash == WorldHashes.GraphicObject)
            {
                Take(FcbEntityFields.ReadString(slot, WorldHashes.TextObjModel));
            }
        }

        return paths;
    }

    public static WorldModelSet Load(
        IReadOnlyList<WorldEntity> entities, ArchetypeIndex archetypes,
        Func<string, byte[]?> readByPath, IProgress<string>? progress = null)
    {
        Func<string, MaterialSurface?> surfaceByMaterial = SurfaceResolver(readByPath);

        // A few hundred archetypes cover ~90k entities, so the fallback walk runs once per name.
        var archetypeMeshPaths = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var pathsByEntity = new Dictionary<WorldEntity, IReadOnlyList<string>>();
        int withoutMesh = 0;
        foreach (WorldEntity entity in entities)
        {
            if (entity.Position is null)
            {
                continue;
            }

            IReadOnlyList<string> paths = MeshPaths(entity.Node);
            if (paths.Count == 0 && entity.ArchetypeName.Length > 0)
            {
                if (!archetypeMeshPaths.TryGetValue(entity.ArchetypeName, out IReadOnlyList<string>? cached))
                {
                    cached = archetypes.Winner(entity.ArchetypeName) is { } winner ? MeshPaths(winner.Node) : [];
                    archetypeMeshPaths[entity.ArchetypeName] = cached;
                }

                paths = cached;
            }

            if (paths.Count > 0)
            {
                pathsByEntity[entity] = paths;
            }
            else
            {
                withoutMesh++;
            }
        }

        List<string> unique = [.. pathsByEntity.Values.SelectMany(p => p).Distinct(StringComparer.OrdinalIgnoreCase)];
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
                    model = Bake(path, XbgModel.Parse(bytes), FineTriangleBudget, surfaceByMaterial);
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

        var modelIndicesByEntity = new Dictionary<WorldEntity, int[]>(pathsByEntity.Count);
        foreach ((WorldEntity entity, IReadOnlyList<string> paths) in pathsByEntity)
        {
            int[] indices = [.. paths
                .Select(p => indexByPath.TryGetValue(p, out int index) ? index : -1)
                .Where(index => index >= 0)];
            if (indices.Length > 0)
            {
                modelIndicesByEntity[entity] = indices;
            }
        }

        progress?.Report(
            $"Baked {models.Count:N0} of {unique.Count:N0} meshes for {modelIndicesByEntity.Count:N0} entities");
        return new WorldModelSet
        {
            Models = models,
            ModelIndicesByEntity = modelIndicesByEntity,
            FailedPathCount = unique.Count - models.Count,
            EntitiesWithoutMesh = withoutMesh,
        };
    }

    /// <summary>Null when the parse yielded no drawable triangles (DNKS mismatch or empty mesh).</summary>
    public static WorldModel? Bake(
        string path, XbgModel model, int fineTriangleBudget, Func<string, MaterialSurface?>? surfaceByMaterial = null)
    {
        // Measured on what each tier would actually draw, fallback parts included, so the budget
        // means the same thing as the bake.
        var submeshesPerLod = new Dictionary<int, List<XbgSubmesh>>();
        var trianglesPerLod = new Dictionary<int, int>();
        foreach (int lod in model.LodLevels)
        {
            List<XbgSubmesh> at = SubmeshesAt(model, lod);
            submeshesPerLod[lod] = at;
            trianglesPerLod[lod] = at.Sum(s => s.Indices.Length) / 3;
        }

        List<int> drawable = [.. trianglesPerLod.Where(p => p.Value > 0).Select(p => p.Key)];
        if (drawable.Count == 0)
        {
            return null;
        }

        int coarseLod = drawable.MinBy(l => (trianglesPerLod[l], -l));
        List<int> fitting = [.. drawable.Where(l => trianglesPerLod[l] <= fineTriangleBudget)];
        int fineLod = fitting.Count > 0 ? fitting.MaxBy(l => (trianglesPerLod[l], -l)) : coarseLod;

        List<XbgSubmesh> fineSubmeshes = submeshesPerLod[fineLod];
        List<XbgSubmesh> coarseSubmeshes = coarseLod == fineLod ? [] : submeshesPerLod[coarseLod];

        int indexCount = fineSubmeshes.Concat(coarseSubmeshes).Sum(s => s.Indices.Length);
        var vertices = new List<float>(indexCount * WorldModel.FloatsPerVertex / 3);
        var indices = new List<int>(indexCount);
        var materialRanges = new List<MaterialRange>();
        IndexRange fine = BakeLod(fineSubmeshes, vertices, indices, materialRanges, surfaceByMaterial);
        IndexRange coarse = coarseLod == fineLod
            ? fine
            : BakeLod(coarseSubmeshes, vertices, indices, null, null);

        var localMin = new Vector3(float.MaxValue);
        var localMax = new Vector3(float.MinValue);
        for (int i = 0; i + 2 < vertices.Count; i += WorldModel.FloatsPerVertex)
        {
            var p = new Vector3(vertices[i], vertices[i + 1], vertices[i + 2]);
            localMin = Vector3.Min(localMin, p);
            localMax = Vector3.Max(localMax, p);
        }

        return new WorldModel
        {
            Path = path,
            Vertices = [.. vertices],
            Indices = [.. indices],
            Fine = fine,
            Coarse = coarse,
            MaterialRanges = materialRanges,
            LocalMin = localMin.X > localMax.X ? Vector3.Zero : localMin,
            LocalMax = localMin.X > localMax.X ? Vector3.Zero : localMax,
        };
    }

    /// <summary>Parts do not all carry the same LOD levels - a wall or a wheel often stops at a
    /// finer one than the body it belongs to - so a part with nothing at the requested level falls
    /// back to its own nearest rather than dropping out of the model.</summary>
    private static List<XbgSubmesh> SubmeshesAt(XbgModel model, int lod)
    {
        var picked = new List<XbgSubmesh>();
        foreach (IGrouping<string, XbgSubmesh> part in model.Submeshes
            .Where(s => s.Indices.Length > 0)
            .GroupBy(s => s.PartName, StringComparer.OrdinalIgnoreCase))
        {
            // Ties break toward the finer level, so the second key is the LOD itself.
            int nearest = part.MinBy(s => (Math.Abs(s.LodLevel - lod), s.LodLevel))!.LodLevel;
            picked.AddRange(part.Where(s => s.LodLevel == nearest));
        }

        return picked;
    }

    private static IndexRange BakeLod(
        List<XbgSubmesh> submeshes, List<float> vertices, List<int> indices,
        List<MaterialRange>? materialRanges, Func<string, MaterialSurface?>? surfaceByMaterial)
    {
        int rangeStart = indices.Count;

        // Submeshes on one part share a Positions array by reference, but parts sharing a buffer
        // each bake their own placement into it - so a block is keyed by buffer and placement
        // together, and emits only the vertices its own triangles reach.
        var blocks = new Dictionary<(object Buffer, Matrix4x4? Placement), (int[] Remap, int Base)>();
        foreach (IGrouping<(object Buffer, Matrix4x4? Placement), XbgSubmesh> block in
            submeshes.GroupBy(s => ((object)s.Positions, s.PartTransform)))
        {
            XbgSubmesh first = block.First();
            int[] remap = new int[first.Positions.Length];
            Array.Fill(remap, -1);
            var source = new List<int>();
            foreach (XbgSubmesh submesh in block)
            {
                foreach (int index in submesh.Indices)
                {
                    if (remap[index] < 0)
                    {
                        remap[index] = source.Count;
                        source.Add(index);
                    }
                }
            }

            Vector3[] normals = first.Normals ?? XbgModel.ComputeSmoothNormals(
                first.Positions, [.. block.SelectMany(s => s.Indices)]);
            int baseIndex = vertices.Count / WorldModel.FloatsPerVertex;
            foreach (int i in source)
            {
                Vector3 p = first.Place(first.Positions[i]);
                Vector3 n = first.PlaceNormal(normals[i]);
                Vector2 uv = first.Uvs is { } uvs ? uvs[i] : Vector2.Zero;
                // Only the mask's blue channel reaches the diffuse blend; absent, the engine reads
                // white, which lands on the material's DiffuseColor1.
                float mask = first.Colours is { } colours ? colours[i].Z : 1f;
                vertices.Add(p.X);
                vertices.Add(p.Y);
                vertices.Add(p.Z);
                vertices.Add(n.X);
                vertices.Add(n.Y);
                vertices.Add(n.Z);
                vertices.Add(uv.X);
                vertices.Add(uv.Y);
                vertices.Add(mask);
            }

            blocks[block.Key] = (remap, baseIndex);
        }

        // Indices grouped by material, so each material's triangles form one contiguous range.
        foreach (IGrouping<int, XbgSubmesh> byMaterial in submeshes.GroupBy(s => s.MaterialIndex).OrderBy(g => g.Key))
        {
            int materialStart = indices.Count;
            foreach (XbgSubmesh submesh in byMaterial)
            {
                (int[] remap, int baseIndex) = blocks[((object)submesh.Positions, submesh.PartTransform)];
                foreach (int index in submesh.Indices)
                {
                    indices.Add(baseIndex + remap[index]);
                }
            }

            string materialName = byMaterial.First().MaterialName;
            materialRanges?.Add(new MaterialRange(
                materialStart, indices.Count - materialStart, materialName,
                surfaceByMaterial?.Invoke(materialName) ?? MaterialSurface.None));
        }

        return new IndexRange(rangeStart, indices.Count - rangeStart);
    }

    /// <summary>
    /// The per-material lookup the bake consumes. A mesh's material table stores the .xbm's archive
    /// path, so each referenced material is read directly and remembered - materials are shared
    /// across meshes.
    /// </summary>
    public static Func<string, MaterialSurface?> SurfaceResolver(Func<string, byte[]?> readByPath)
    {
        var cache = new ConcurrentDictionary<string, MaterialSurface?>(StringComparer.OrdinalIgnoreCase);
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
                    return SurfaceOf(XbmMaterial.Parse(bytes));
                }
                catch (Exception)
                {
                    return null;
                }
            });
    }

    /// <summary>The engine keeps its flat colour swatches in one folder - an 8x8 grey, white, red
    /// and black, plus a few gradients. Pointing layer 1 at one of those is not a request for a
    /// grey surface: the detail sits in layer 2, which the shader blends over the swatch. 51 retail
    /// materials are built that way, the swamp boat's hull among them.</summary>
    private const string SwatchFolder = @"graphics\_textures\diffuse\icone\";

    /// <summary>What one material draws as. Layer 1 is the albedo, except where it is a flat swatch
    /// standing in for a second layer, in which case layer 2 and its own tint take over.</summary>
    public static MaterialSurface SurfaceOf(XbmMaterial material)
    {
        string? albedo = DiffuseTextureOf(material);
        (Vector3 tintBase, Vector3 tint) = TintsOf(material);

        if (albedo is not null && albedo.StartsWith(SwatchFolder, StringComparison.OrdinalIgnoreCase) &&
            TextureSlot(material, "DiffuseTexture2") is { } second &&
            !second.Equals(albedo, StringComparison.OrdinalIgnoreCase))
        {
            // Layer 2 carries its own colour rather than the pair layer 1 lerps between, so both
            // ends of the lerp become it and the per-vertex blend flattens out.
            albedo = second;
            Vector3 layer2 = ColourProperty(material, "DiffuseColor2") ?? Vector3.One;
            (tintBase, tint) = (layer2, layer2);
        }

        return new MaterialSurface(albedo, AlphaOf(material), tintBase, tint);
    }

    /// <summary>
    /// The pair the engine's shader lerps between per vertex, as
    /// <c>diffuseMap * lerp(DiffuseColorBase, DiffuseColor1, vertexColour.b)</c>. Tints run past 1
    /// on 295 retail materials, so they are floored at zero and never capped. A material naming
    /// only one of the two uses it for both, which reproduces a flat tint.
    /// </summary>
    public static (Vector3 Base, Vector3 Tint) TintsOf(XbmMaterial material)
    {
        Vector3? tint = ColourProperty(material, "DiffuseColor1");
        Vector3? tintBase = ColourProperty(material, "DiffuseColorBase");
        return (tintBase ?? tint ?? Vector3.One, tint ?? tintBase ?? Vector3.One);
    }

    private static Vector3? ColourProperty(XbmMaterial material, string key)
    {
        if (material.Properties.FirstOrDefault(
            p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is not { } property)
        {
            return null;
        }

        string[] parts = property.Value.Split(',');
        if (parts.Length < 3)
        {
            return null;
        }

        var colour = new Vector3();
        for (int i = 0; i < 3; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                return null;
            }

            colour[i] = Math.Max(0f, v);
        }

        return colour;
    }

    /// <summary>Blending outranks testing where a material sets both. A material that declares
    /// neither - 86% of the retail set - is opaque.</summary>
    public static MaterialAlpha AlphaOf(XbmMaterial material)
    {
        bool Flag(string key) => material.Properties
            .FirstOrDefault(p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is { } property
            && int.TryParse(property.Value, out int value) && value != 0;

        return Flag("AlphaBlendEnabled") ? MaterialAlpha.Blend
            : Flag("AlphaTestEnabled") ? MaterialAlpha.Mask
            : MaterialAlpha.Opaque;
    }

    /// <summary>The albedo across the material templates: Generic and Hair bind DiffuseTexture1,
    /// Skin puts it in SkinTexture, Cloth in FabricTexture.</summary>
    public static string? DiffuseTextureOf(XbmMaterial material)
    {
        if ((TextureSlot(material, "DiffuseTexture1") ?? TextureSlot(material, "SkinTexture")
            ?? TextureSlot(material, "FabricTexture")) is { } named)
        {
            return named;
        }

        string? any = material.Textures.FirstOrDefault(
            t => t.Key.StartsWith("DiffuseTexture", StringComparison.OrdinalIgnoreCase))?.Value;
        return any is { Length: > 0 } ? NameHash.Normalize(any) : null;
    }

    /// <summary>One named texture slot, in the archive's normalized path form.</summary>
    private static string? TextureSlot(XbmMaterial material, string key)
    {
        string? value = material.Textures
            .FirstOrDefault(t => t.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
        return value is { Length: > 0 } ? NameHash.Normalize(value) : null;
    }
}
