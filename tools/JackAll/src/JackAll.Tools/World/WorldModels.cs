using System.Collections.Concurrent;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
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
/// What one .xbm contributes to a draw, in the terms the engine's own <c>Generic</c> pixel shader
/// uses. Two diffuse layers and a mask, where the mask decides both how much of each layer shows and
/// which of the first layer's two tints it takes:
/// <code>
/// layer1 = diffuse1 * lerp(TintBase, Tint, mask.b * vertexColour.b)
/// layer2 = diffuse2 * SecondTint
/// albedo = lerp(layer1, layer2, mask.g * vertexColour.g)
/// </code>
/// Decoded from the retail shader objects rather than inferred - see the notes on
/// <see cref="WorldModels.SurfaceOf"/>.
/// </summary>
public readonly record struct MaterialSurface
{
    /// <summary>Layer 1's albedo, and the only layer whose alpha is coverage.</summary>
    public string? DiffuseTexturePath { get; init; }

    /// <summary>Layer 2's albedo, blended over layer 1 by the mask. Null leaves layer 1 alone.</summary>
    public string? SecondDiffusePath { get; init; }

    /// <summary>The mask driving both blends. Null makes the material a plain tinted layer 1, which
    /// is what the engine gets when the mask samples white.</summary>
    public string? MaskPath { get; init; }

    public MaterialAlpha Alpha { get; init; }

    /// <summary>The pair layer 1's tint lerps between, unclamped - retail authors past 1.</summary>
    public Vector3 TintBase { get; init; }
    public Vector3 Tint { get; init; }

    /// <summary>Layer 2's single tint.</summary>
    public Vector3 SecondTint { get; init; }

    /// <summary>UV multipliers, one per texture. Every one of these is a real number in retail - 55%
    /// of materials tile at something other than 1, up to 20x.</summary>
    public Vector2 DiffuseTiling { get; init; }
    public Vector2 SecondDiffuseTiling { get; init; }
    public Vector2 MaskTiling { get; init; }

    /// <summary>What an unresolved material draws as: the texture unchanged, untiled, untinted.</summary>
    public static readonly MaterialSurface None = new()
    {
        Alpha = MaterialAlpha.Opaque,
        TintBase = Vector3.One,
        Tint = Vector3.One,
        SecondTint = Vector3.One,
        DiffuseTiling = Vector2.One,
        SecondDiffuseTiling = Vector2.One,
        MaskTiling = Vector2.One,
    };
}

/// <summary>
/// One graphics slot: the mesh it names, and which of that mesh's parts to draw.
/// </summary>
/// <remarks>
/// An empty <see cref="Parts"/> draws the whole mesh, which is what almost every entity wants. The
/// exception is a wardrobe file: <c>merc_kit.xbg</c> holds all 111 pieces a mercenary can be built
/// from - every head, hat, shirt and pair of boots - and each NPC's <c>hidMeshName</c> lists the
/// dozen it actually wears. Drawing the file whole gives one body wearing seventeen faces.
/// </remarks>
public readonly record struct MeshRef(string Path, string Parts)
{
    /// <summary>The parts as the file writes them: a semicolon-delimited list with empty ends.
    /// Normalized to a plain uppercase list so it can key a bake.</summary>
    public static string ParseParts(string hidMeshName)
        => string.Join(';', hidMeshName
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.ToUpperInvariant())
            .Order(StringComparer.Ordinal));

    /// <summary>The part names as a set, or null when this slot draws the whole mesh.</summary>
    public IReadOnlySet<string>? PartSet()
        => Parts.Length == 0
            ? null
            : new HashSet<string>(Parts.Split(';'), StringComparer.OrdinalIgnoreCase);
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
/// [px py pz nx ny nz u v maskG maskB] vertices and a triangle index list holding two detail tiers.
/// </summary>
public sealed class WorldModel
{
    public const int FloatsPerVertex = 10;

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
        => [.. MeshRefs(entityNode).Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>The same slots, each with the parts of its mesh the entity actually wears.</summary>
    public static IReadOnlyList<MeshRef> MeshRefs(FcbObject entityNode)
    {
        if (FcbEntityFields.FindComponent(entityNode, WorldHashes.CGraphicComponent) is not { } component)
        {
            return [];
        }

        var refs = new List<MeshRef>();
        void Take(FcbObject holder)
        {
            string value = FcbEntityFields.ReadString(holder, WorldHashes.TextObjModel);
            if (value.Length == 0)
            {
                return;
            }

            var slot = new MeshRef(
                NameHash.Normalize(value),
                MeshRef.ParseParts(FcbEntityFields.ReadString(holder, WorldHashes.HidMeshName)));
            if (!refs.Contains(slot))
            {
                refs.Add(slot);
            }
        }

        Take(component);
        foreach (FcbObject slot in component.Children)
        {
            if (slot.TypeHash == WorldHashes.GraphicObject)
            {
                Take(slot);
            }
        }

        return refs;
    }

    public static WorldModelSet Load(
        IReadOnlyList<WorldEntity> entities, ArchetypeIndex archetypes,
        Func<string, byte[]?> readByPath, IProgress<string>? progress = null)
    {
        Func<string, MaterialSurface?> surfaceByMaterial = SurfaceResolver(readByPath);

        // A few hundred archetypes cover ~90k entities, so the fallback walk runs once per name.
        var archetypeMeshRefs = new Dictionary<string, IReadOnlyList<MeshRef>>(StringComparer.OrdinalIgnoreCase);
        var refsByEntity = new Dictionary<WorldEntity, IReadOnlyList<MeshRef>>();
        int withoutMesh = 0;
        foreach (WorldEntity entity in entities)
        {
            if (entity.Position is null)
            {
                continue;
            }

            IReadOnlyList<MeshRef> refs = MeshRefs(entity.Node);
            if (refs.Count == 0 && entity.ArchetypeName.Length > 0)
            {
                if (!archetypeMeshRefs.TryGetValue(entity.ArchetypeName, out IReadOnlyList<MeshRef>? cached))
                {
                    cached = archetypes.Winner(entity.ArchetypeName) is { } winner ? MeshRefs(winner.Node) : [];
                    archetypeMeshRefs[entity.ArchetypeName] = cached;
                }

                refs = cached;
            }

            if (refs.Count > 0)
            {
                refsByEntity[entity] = refs;
            }
            else
            {
                withoutMesh++;
            }
        }

        CollapseCrowdedWardrobes(refsByEntity);

        // Keyed by slot rather than by path, because two entities wearing different outfits out of
        // one wardrobe file bake to different geometry. The parse is still shared per path, so a
        // file every mercenary in the world references is read and parsed exactly once.
        List<MeshRef> unique = [.. refsByEntity.Values.SelectMany(r => r).Distinct()];
        var parsed = new ConcurrentDictionary<string, XbgModel?>(StringComparer.OrdinalIgnoreCase);
        var baked = new ConcurrentDictionary<MeshRef, WorldModel?>();
        int done = 0;
        Parallel.ForEach(unique, slot =>
        {
            WorldModel? model = null;
            // A single corrupt or unexpected file must not take down the world load.
            try
            {
                XbgModel? mesh = parsed.GetOrAdd(slot.Path, path =>
                {
                    try
                    {
                        return readByPath(path) is { } bytes ? XbgModel.Parse(bytes) : null;
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                });

                if (mesh is not null)
                {
                    model = Bake(slot.Path, mesh, FineTriangleBudget, surfaceByMaterial, slot.PartSet());
                }
            }
            catch (Exception)
            {
            }

            baked[slot] = model;
            int soFar = Interlocked.Increment(ref done);
            if (soFar % 200 == 0)
            {
                progress?.Report($"Loading models: {soFar:N0}/{unique.Count:N0}");
            }
        });

        var models = new List<WorldModel>();
        var indexByPath = new Dictionary<MeshRef, int>();
        foreach (MeshRef slot in unique)
        {
            if (baked[slot] is { } model)
            {
                indexByPath[slot] = models.Count;
                models.Add(model);
            }
        }

        var modelIndicesByEntity = new Dictionary<WorldEntity, int[]>(refsByEntity.Count);
        foreach ((WorldEntity entity, IReadOnlyList<MeshRef> refs) in refsByEntity)
        {
            int[] indices = [.. refs
                .Select(r => indexByPath.TryGetValue(r, out int index) ? index : -1)
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
        string path, XbgModel model, int fineTriangleBudget,
        Func<string, MaterialSurface?>? surfaceByMaterial = null, IReadOnlySet<string>? onlyParts = null)
    {
        // Measured on what each tier would actually draw, fallback parts included, so the budget
        // means the same thing as the bake.
        var submeshesPerLod = new Dictionary<int, List<XbgSubmesh>>();
        var trianglesPerLod = new Dictionary<int, int>();
        foreach (int lod in model.LodLevels)
        {
            List<XbgSubmesh> at = SubmeshesAt(model, lod, onlyParts);
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
    /// <summary>
    /// How many different part lists one mesh may bake before they all collapse to one.
    /// </summary>
    /// <remarks>
    /// Filtering parts out never costs memory - it is the number of copies that does. Each distinct
    /// outfit is a separate bake of the same file, so a mesh worn one way is 2 MB and the same mesh
    /// worn 682 ways is 137 MB. A handful of variants is free and worth keeping; a wardrobe with
    /// hundreds is not, and every NPC wearing one default outfit still beats drawing the whole rack.
    /// </remarks>
    public const int MaxOutfitsPerMesh = 16;

    /// <summary>
    /// Holds the per-entity outfits of any mesh with few enough of them, and gives every entity
    /// wearing a more crowded one its most common outfit instead. Ties break on the part list so a
    /// world always loads the same way.
    /// </summary>
    private static void CollapseCrowdedWardrobes(Dictionary<WorldEntity, IReadOnlyList<MeshRef>> refsByEntity)
    {
        var uses = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (MeshRef slot in refsByEntity.Values.SelectMany(refs => refs))
        {
            if (slot.Parts.Length == 0)
            {
                continue;
            }

            if (!uses.TryGetValue(slot.Path, out Dictionary<string, int>? outfits))
            {
                uses[slot.Path] = outfits = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            outfits[slot.Parts] = outfits.GetValueOrDefault(slot.Parts) + 1;
        }

        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, Dictionary<string, int> outfits) in uses)
        {
            if (outfits.Count > MaxOutfitsPerMesh)
            {
                defaults[path] = outfits
                    .OrderByDescending(o => o.Value).ThenBy(o => o.Key, StringComparer.Ordinal)
                    .First().Key;
            }
        }

        if (defaults.Count == 0)
        {
            return;
        }

        foreach (WorldEntity entity in refsByEntity.Keys.ToList())
        {
            IReadOnlyList<MeshRef> refs = refsByEntity[entity];
            if (!refs.Any(r => r.Parts.Length > 0 && defaults.ContainsKey(r.Path)))
            {
                continue;
            }

            refsByEntity[entity] = [.. refs.Select(r =>
                r.Parts.Length > 0 && defaults.TryGetValue(r.Path, out string? only)
                    ? r with { Parts = only }
                    : r)];
        }
    }

    /// <summary>The <c>STATE&lt;n&gt;</c> tag a part name carries, anywhere in the name.</summary>
    private static readonly Regex StateToken =
        new(@"_?STATE(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// One variant per part. A file holds every state a part can be in - a vehicle body intact and
    /// wrecked, a door closed, ajar and open, a sign whole and snapped - and the engine shows one at
    /// a time, so drawing them all buries a pristine hull inside its own wreck.
    /// </summary>
    /// <remarks>
    /// Each part keeps the lowest state number it has, which is the intact, closed, unbroken one.
    /// The comparison is per part rather than per file because 616 part groups in the retail set
    /// have no state 1 at all - a file-wide minimum would delete them outright. A part naming no
    /// state is never touched. Across the corpus this drops 364 parts over 181 meshes.
    /// </remarks>
    private static List<XbgSubmesh> PristineOnly(List<XbgSubmesh> submeshes)
    {
        var lowest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (XbgSubmesh submesh in submeshes)
        {
            if (StateOf(submesh.PartName) is not int state)
            {
                continue;
            }

            string group = StateToken.Replace(submesh.PartName, "");
            lowest[group] = lowest.TryGetValue(group, out int best) ? Math.Min(best, state) : state;
        }

        return lowest.Count == 0
            ? submeshes
            : [.. submeshes.Where(s => StateOf(s.PartName) is not int state
                || state == lowest[StateToken.Replace(s.PartName, "")])];
    }

    private static int? StateOf(string partName)
        => StateToken.Match(partName) is { Success: true } match
            && int.TryParse(match.Groups[1].ValueSpan, out int state)
                ? state
                : null;

    private static List<XbgSubmesh> SubmeshesAt(XbgModel model, int lod, IReadOnlySet<string>? onlyParts)
    {
        var picked = new List<XbgSubmesh>();
        foreach (IGrouping<string, XbgSubmesh> part in PristineOnly([.. model.Submeshes
            .Where(s => s.Indices.Length > 0 && (onlyParts is null || onlyParts.Contains(s.PartName)))])
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
                // Two of the vertex colour's channels reach the diffuse: green weights the blend
                // between the material's two layers, blue the lerp between layer 1's two tints. Both
                // are multiplied by a mask channel in the shader. Absent, the engine reads white.
                Vector4 colour = first.Colours is { } colours ? colours[i] : Vector4.One;
                vertices.Add(p.X);
                vertices.Add(p.Y);
                vertices.Add(p.Z);
                vertices.Add(n.X);
                vertices.Add(n.Y);
                vertices.Add(n.Z);
                vertices.Add(uv.X);
                vertices.Add(uv.Y);
                vertices.Add(colour.Y);
                vertices.Add(colour.Z);
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

    /// <summary>
    /// What one material draws as, following the retail <c>Generic</c> pixel shader.
    /// </summary>
    /// <remarks>
    /// Decoded from <c>shadersobj\engine\shaders\obj10</c>, which ships the D3D10 build of the
    /// engine's shaders as DXBC with its reflection chunk intact - so the constant and sampler names
    /// survive and <c>fxc /dumpbin</c> reads them straight out. The sibling <c>obj</c> tree is the
    /// same shaders built for D3D9 with the names stripped.
    /// <para>
    /// One simplification: the engine pairs every tiling with a "group" that picks which of the two
    /// UV sets the texture reads. Group 0 maps to UV set 0 on every retail material, and layer 1
    /// always sits on group 0, so layer 1 is exact. Which group the mask and layer 2 use is not
    /// recorded in the .xbm - only the group-to-channel table is - so both are read off UV set 0
    /// too, which is right wherever their group also maps to channel 0.
    /// </para>
    /// </remarks>
    public static MaterialSurface SurfaceOf(XbmMaterial material)
    {
        (Vector3 tintBase, Vector3 tint) = TintsOf(material);
        return new MaterialSurface
        {
            DiffuseTexturePath = DiffuseTextureOf(material),
            SecondDiffusePath = TextureSlot(material, "DiffuseTexture2"),
            MaskPath = TextureSlot(material, "MaskTexture1"),
            Alpha = AlphaOf(material),
            TintBase = tintBase,
            Tint = tint,
            SecondTint = ColourProperty(material, "DiffuseColor2") ?? Vector3.One,
            DiffuseTiling = TilingProperty(material, "DiffuseTiling1"),
            SecondDiffuseTiling = TilingProperty(material, "DiffuseTiling2"),
            MaskTiling = TilingProperty(material, "MaskTiling1"),
        };
    }

    /// <summary>A material's UV multiplier for one texture; 1:1 when it names none.</summary>
    private static Vector2 TilingProperty(XbmMaterial material, string key)
    {
        if (material.Properties.FirstOrDefault(
            p => p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is not { } property)
        {
            return Vector2.One;
        }

        string[] parts = property.Value.Split(',');
        return parts.Length >= 2
            && float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float u)
            && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)
            && u != 0 && v != 0
                ? new Vector2(u, v)
                : Vector2.One;
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
