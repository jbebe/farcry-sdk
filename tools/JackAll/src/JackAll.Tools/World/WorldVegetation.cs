using System.Collections.Concurrent;
using System.Numerics;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Xbg;

namespace JackAll.Tools.World;

/// <summary>One placed plant: where it stands and which resource it instantiates.</summary>
public sealed record VegetationInstance(Vector3 Position, uint ResourceId);

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
/// <remarks>
/// Field packing follows <c>StSerialVegetationZoneData::RestoreGraphicCluster</c> and the
/// <c>CollectionComponentSerialUtils</c> helpers beside it:
/// <c>posXYList</c> holds two <c>u16</c> per instance in decimetres of GLOBAL world space,
/// <c>posZList</c> a plain float, and <c>clustersInstancesCntList</c> packs an instance count in the
/// low byte with the instance-array offset in the upper 24 bits.
/// </remarks>
public static class WorldVegetation
{
    private const float DecimetresToMetres = 0.1f;

    /// <summary>
    /// Which mesh each scatter resource id names.
    /// </summary>
    /// <remarks>
    /// A resource id is the CRC32 of the resource's own path - the same hash the archives key on -
    /// so reversing it over the known path list resolves every id the retail data uses. Only meshes
    /// come back: about three quarters of placed instances name a <c>.rtx</c> RealTree, a procedural
    /// tree format this preview has no parser for, and those stay markers. The remaining quarter is
    /// real geometry - the desert rocks, grasses, facing bushes and river pebbles.
    /// </remarks>
    public static Dictionary<uint, string> MeshesByResourceId(IEnumerable<string> knownPaths)
    {
        var byId = new Dictionary<uint, string>();
        foreach (string path in knownPaths)
        {
            if (!path.EndsWith(".xbg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string normalized = NameHash.Normalize(path);
            byId[NameHash.Compute(normalized)] = normalized;
        }

        return byId;
    }

    public static IReadOnlyList<VegetationInstance> Load(
        TerrainMap map, Func<string, byte[]?> readByPath, FcbClassDefinitions definitions,
        IProgress<string>? progress = null)
    {
        progress?.Report($"Loading {map.Name} vegetation");

        var perSector = new List<VegetationInstance>[map.Sectors.Count];
        Parallel.For(0, map.Sectors.Count, index =>
        {
            (string path, int sectorId) = map.Sectors[index];
            string sectorDir = path.Replace(@"\sdat\", @"\worldsectors\", StringComparison.OrdinalIgnoreCase);
            var found = new List<VegetationInstance>();

            // Note the naming: the far file has an underscore before its id, the near file does not.
            foreach (string file in new[] { $"landmarkfar_{sectorId}.data.fcb", $"landmarknear{sectorId}.data.fcb" })
            {
                string landmark = sectorDir.Replace($"sd{sectorId}.sdat", file, StringComparison.OrdinalIgnoreCase);
                // TryDeserialize because the path is a derived probe - see WorldLoader.Load.
                if (readByPath(landmark) is not { } bytes || FcbDocument.TryDeserialize(bytes) is not { } root)
                {
                    continue;
                }

                Collect(root, definitions, found, 0);
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
    /// resolves to a mesh becomes a standing entity at its own position, so the model layer's
    /// culling, detail tiers, materials and textures all apply to a rock exactly as they do to a
    /// crate. Instances naming a RealTree come back as <c>Markers</c> for the billboard layer.
    /// </summary>
    public static (WorldModelSet Models, IReadOnlyList<VegetationInstance> Markers) Split(
        IReadOnlyList<VegetationInstance> instances, IReadOnlyDictionary<uint, string> meshesById,
        Func<string, byte[]?> readByPath, IProgress<string>? progress = null)
    {
        var markers = new List<VegetationInstance>();
        var drawable = new List<(VegetationInstance Instance, string Path)>();
        foreach (VegetationInstance instance in instances)
        {
            if (meshesById.TryGetValue(instance.ResourceId, out string? path))
            {
                drawable.Add((instance, path));
            }
            else
            {
                markers.Add(instance);
            }
        }

        progress?.Report($"Loading vegetation models: {drawable.Count:N0} instances");

        Func<string, MaterialSurface?> surfaceByMaterial = WorldModels.SurfaceResolver(readByPath);
        string[] unique = [.. drawable.Select(d => d.Path).Distinct(StringComparer.OrdinalIgnoreCase)];
        var baked = new ConcurrentDictionary<string, WorldModel?>(StringComparer.OrdinalIgnoreCase);
        Parallel.ForEach(unique, path =>
        {
            try
            {
                baked[path] = readByPath(path) is { } bytes
                    ? WorldModels.Bake(path, XbgModel.Parse(bytes), WorldModels.FineTriangleBudget, surfaceByMaterial)
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
            if (baked[path] is { } model)
            {
                indexByPath[path] = models.Count;
                models.Add(model);
            }
        }

        // The model layer keys instances on entities, so each plant gets one. They all share a
        // single node and document: nothing reads either, and 40,000 copies of both would be waste.
        var host = new WorldSectorDocument
        {
            SourcePath = "",
            SectorId = 0,
            PristineRoot = new FcbObject { TypeHash = WorldHashes.Entity },
        };

        var byEntity = new Dictionary<WorldEntity, int[]>(drawable.Count);
        foreach ((VegetationInstance instance, string path) in drawable)
        {
            if (!indexByPath.TryGetValue(path, out int index))
            {
                markers.Add(instance);
                continue;
            }

            byEntity[new WorldEntity
            {
                Node = host.PristineRoot,
                HomeSector = host,
                LayerPathId = "",
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                ArchetypeName = path,
                Position = instance.Position,
            }] = [index];
        }

        var set = new WorldModelSet
        {
            Models = models,
            ModelIndicesByEntity = byEntity,
            FailedPathCount = unique.Length - models.Count,
            EntitiesWithoutMesh = markers.Count,
        };

        progress?.Report(
            $"Loaded vegetation: {byEntity.Count:N0} models over {models.Count} meshes, {markers.Count:N0} markers");
        return (set, markers);
    }

    private static void Collect(
        FcbObject node, FcbClassDefinitions definitions, List<VegetationInstance> into, int depth)
    {
        if ((definitions.GetClass(node.TypeHash).Name ?? "") == "VegetationZoneData")
        {
            ReadZone(node, definitions, into);
            return;
        }

        // A file holds several zones, so every branch has to be walked.

        if (depth > 10)
        {
            return;
        }
        foreach (FcbObject child in node.Children)
        {
            Collect(child, definitions, into, depth + 1);
        }
    }

    private static void ReadZone(FcbObject zone, FcbClassDefinitions definitions, List<VegetationInstance> into)
    {
        if (Field(zone, definitions, "posXYList") is not { } xy ||
            Field(zone, definitions, "posZList") is not { } z)
        {
            return;
        }

        // Which resource an instance uses comes from walking the clusters: each cluster names an
        // instance range, and the resource lists say which resource owns which cluster.
        uint[] resources = Elements(Field(zone, definitions, "resourceList"));
        uint[] clustersPerResource = Elements(Field(zone, definitions, "resClustersCntList"));
        uint[] clusters = Elements(Field(zone, definitions, "clustersInstancesCntList"));

        var resourceOfCluster = new uint[clusters.Length];
        int cluster = 0;
        for (int resource = 0; resource < clustersPerResource.Length && cluster < clusters.Length; resource++)
        {
            for (uint n = 0; n < clustersPerResource[resource] && cluster < clusters.Length; n++)
            {
                resourceOfCluster[cluster++] = resource < resources.Length ? resources[resource] : 0;
            }
        }

        int instances = Math.Min((xy.Length - 4) / 4, (z.Length - 4) / 4);
        for (int i = 0; i < clusters.Length; i++)
        {
            uint count = clusters[i] & 0xFF;
            uint offset = clusters[i] >> 8;
            for (uint n = 0; n < count; n++)
            {
                int instance = (int)(offset + n);
                if (instance >= instances)
                {
                    continue;
                }

                uint packed = BitConverter.ToUInt32(xy, 4 + instance * 4);
                into.Add(new VegetationInstance(
                    new Vector3(
                        (packed & 0xFFFF) * DecimetresToMetres,
                        (packed >> 16) * DecimetresToMetres,
                        BitConverter.ToSingle(z, 4 + instance * 4)),
                    resourceOfCluster[i]));
            }
        }
    }

    /// <summary>Every list is a <c>u32</c> count followed by that many 4-byte elements.</summary>
    private static uint[] Elements(byte[]? raw)
    {
        if (raw is null || raw.Length < 8)
        {
            return [];
        }

        int count = Math.Min(BitConverter.ToInt32(raw, 0), (raw.Length - 4) / 4);
        var values = new uint[Math.Max(count, 0)];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BitConverter.ToUInt32(raw, 4 + i * 4);
        }
        return values;
    }

    private static byte[]? Field(FcbObject node, FcbClassDefinitions definitions, string name)
    {
        FcbClass declaring = definitions.GetClass(node.TypeHash);
        foreach ((uint hash, byte[] value) in node.Values)
        {
            if ((declaring.FindMember(hash)?.Name ?? "") == name)
            {
                return value;
            }
        }
        return null;
    }
}
