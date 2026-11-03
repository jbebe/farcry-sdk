using System.Collections.Concurrent;
using System.Numerics;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Rtx;
using JackAll.Tools.Xbg;

namespace JackAll.Tools.World;

/// <summary>One placed plant: where it stands and which resource it instantiates.</summary>
public sealed record VegetationInstance(Vector3 Position, uint ResourceId);

/// <summary>
/// One placed copy of one baked mesh, with no object behind it. World 1 scatters 2.4 million of
/// these - an entity apiece would cost a few hundred megabytes of objects and as many tiny arrays,
/// so the scatter carries positions and a model index and nothing else.
/// </summary>
public readonly record struct ScatterInstance(Vector3 Position, int Model);

/// <summary>The scatter sorted into what can be drawn and what cannot.</summary>
public sealed record ScatterSet(
    IReadOnlyList<WorldModel> Models, ScatterInstance[] Instances,
    IReadOnlyList<VegetationInstance> Markers);

/// <summary>
/// The vegetation a map places. Each sector's plants live in its two landmark files, under a
/// <c>CCollectionComponent</c>'s <c>VegetationData</c> - never in the sector's own
/// <c>worldsector</c> file.
/// </summary>
/// <remarks>
/// Both landmark files are read. Despite the near/far naming they are not two levels of detail over
/// the same plants: their instance positions do not overlap at all, and the near file carries around
/// twenty times more, so together they are the full set.
/// </remarks>
public static class WorldVegetation
{
    /// <summary>A sector's near landmark file. Note the naming: the far file has an underscore before its id, the near file does not.</summary>
    public static string NearPath(string sdatPath, int sectorId)
        => WorldLoader.SectorFilePath(sdatPath, sectorId, $"landmarknear{sectorId}.data.fcb");

    public static string FarPath(string sdatPath, int sectorId)
        => WorldLoader.SectorFilePath(sdatPath, sectorId, $"landmarkfar_{sectorId}.data.fcb");

    /// <summary>Every zone a landmark file holds; a file holds several, so every branch is walked.</summary>
    public static IEnumerable<VegetationZone> Zones(FcbObject node)
        => node.TypeHash == VegetationZone.TypeHash ? [VegetationZone.Read(node)] : node.Children.SelectMany(Zones);

    /// <summary>
    /// Which resource each scatter id names.
    /// </summary>
    /// <remarks>
    /// A resource id is the CRC32 of the resource's own path - the same hash the archives key on -
    /// so reversing it over the known path list resolves every id the retail data uses. Both kinds
    /// come back. 96% of placed instances are ordinary <c>.xbg</c> - the grasses, desert rocks,
    /// facing bushes and river pebbles - and the rest name a <c>.rtx</c> RealTree, which parses to
    /// triangles of its own (see <see cref="RtxMesh"/>).
    /// </remarks>
    public static Dictionary<uint, string> ResourcesById(IEnumerable<string> knownPaths)
    {
        var byId = new Dictionary<uint, string>();
        foreach (string path in knownPaths)
        {
            if (!path.EndsWith(".xbg", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".rtx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalized = NameHash.Normalize(path);
            byId[NameHash.Compute(normalized)] = normalized;
        }

        return byId;
    }

    public static IReadOnlyList<VegetationInstance> Load(
        TerrainMap map, Func<string, byte[]?> readByPath, IProgress<string>? progress = null)
    {
        progress?.Report($"Loading {map.Name} vegetation");

        var perSector = new List<VegetationInstance>[map.Sectors.Count];
        Parallel.For(0, map.Sectors.Count, index =>
        {
            (string path, int sectorId) = map.Sectors[index];
            var found = new List<VegetationInstance>();

            foreach (string landmark in new[] { FarPath(path, sectorId), NearPath(path, sectorId) })
            {
                // TryDeserialize because the path is a derived probe - see WorldLoader.Load.
                if (readByPath(landmark) is not { } bytes || FcbDocument.TryDeserialize(bytes) is not { } root)
                {
                    continue;
                }

                found.AddRange(
                    from zone in Zones(root)
                    from resource in zone.Resources
                    from cluster in resource.Clusters
                    from tuft in cluster.Tufts
                    select new VegetationInstance(tuft.Position, resource.Id));
            }

            if (found.Count > 0)
            {
                perSector[index] = found;
            }
        });

        var all = new List<VegetationInstance>(perSector.Sum(s => s?.Count ?? 0));
        foreach (List<VegetationInstance>? sector in perSector)
        {
            if (sector is not null)
            {
                all.AddRange(sector);
            }
        }

        progress?.Report($"Loaded {map.Name} vegetation: {all.Count:N0} instances");
        return all;
    }

    /// <summary>
    /// The scatter split into what can be drawn and what cannot: every instance whose resource
    /// resolves to geometry becomes a bare placement of it, so the model layer's culling, detail
    /// tiers, materials and textures all apply to a tuft of grass exactly as they do to a crate.
    /// Only what resolves to nothing at all stays a marker.
    /// </summary>
    /// <remarks>
    /// The scale is the thing to keep in mind here: world 1 places 2.4 million instances, 95% of
    /// them grass, and 96% of the whole scatter resolves to real geometry. Nothing on this path may
    /// allocate per instance beyond the placement itself.
    /// </remarks>
    public static ScatterSet Split(
        IReadOnlyList<VegetationInstance> instances, IReadOnlyDictionary<uint, string> resourcesById,
        Func<string, byte[]?> readByPath, IProgress<string>? progress = null)
    {
        string[] unique = [.. instances
            .Select(i => resourcesById.GetValueOrDefault(i.ResourceId))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        string[] toBake = [.. unique.Where(WorldModels.IsGeometry)];

        progress?.Report($"Loading vegetation: {toBake.Length} meshes");

        Func<string, MaterialSurface?> surfaceByMaterial = WorldModels.SurfaceResolver(readByPath);
        var baked = new ConcurrentDictionary<string, WorldModel?>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(toBake, path =>
        {
            try
            {
                baked[path] = readByPath(path) is { } bytes
                    ? WorldModels.Bake(path, WorldModels.Triangulate(path, bytes), WorldModels.FineTriangleBudget, surfaceByMaterial)
                    : null;
            }
            catch (Exception)
            {
                baked[path] = null;
            }
        });

        var models = new List<WorldModel>();
        var indexByPath = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in unique)
        {
            if (baked.GetValueOrDefault(path) is { } model)
            {
                indexByPath[path] = models.Count;
                models.Add(model);
            }
        }

        var placed = new List<ScatterInstance>(instances.Count);
        var markers = new List<VegetationInstance>();
        foreach (VegetationInstance instance in instances)
        {
            if (resourcesById.TryGetValue(instance.ResourceId, out string? path)
                && indexByPath.TryGetValue(path, out int index))
            {
                placed.Add(new ScatterInstance(instance.Position, index));
            }
            else
            {
                markers.Add(instance);
            }
        }

        progress?.Report(
            $"Loaded vegetation: {placed.Count:N0} placements over {models.Count} meshes, "
            + $"{markers.Count:N0} markers");
        return new ScatterSet(models, [.. placed], markers);
    }
}
