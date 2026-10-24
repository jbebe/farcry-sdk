using System.Collections.Concurrent;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Tools.World;

/// <summary>
/// Builds the in-memory entity model of a map from its <c>worldsector*.data.fcb</c> files, read
/// straight out of the merged filesystem - no extraction.
/// </summary>
public static class WorldLoader
{
    /// <summary>One of a sector's files in <c>worldsectors\</c>, beside its terrain one folder over -
    /// the same derivation the descriptor loader uses, so the level folder never has to be recomputed.</summary>
    public static string SectorFilePath(string sdatPath, int sectorId, string file)
        => sdatPath.Replace(@"\sdat\", @"\worldsectors\", StringComparison.OrdinalIgnoreCase)
            .Replace($"sd{sectorId}.sdat", file, StringComparison.OrdinalIgnoreCase);

    public static string WorldSectorPath(string sdatPath, int sectorId)
        => SectorFilePath(sdatPath, sectorId, $"worldsector{sectorId}.data.fcb");

    /// <param name="everySector">A new world's: every sector with no worldsector file loads as an empty
    /// one, so entities can be placed anywhere on it.</param>
    public static Fc2World Load(
        TerrainMap map,
        Func<string, byte[]?> readByPath,
        IProgress<string>? progress = null,
        bool everySector = false)
    {
        // Three files can place entities in a sector: the worldsector file holds the bulk, and the two
        // landmark files add the set pieces - the buildings whose roofs, doors and window panes ship
        // as separate meshes, plus the forts, churches and HQs. 72 meshes are reachable no other way.
        (string Path, int SectorId, bool Landmark)[] payloads = [.. map.Sectors
            .SelectMany(s => new[]
            {
                (WorldSectorPath(s.Path, s.SectorId), s.SectorId, false),
                (SectorFilePath(s.Path, s.SectorId, $"landmarknear{s.SectorId}.data.fcb"), s.SectorId, true),
                (SectorFilePath(s.Path, s.SectorId, $"landmarkfar_{s.SectorId}.data.fcb"), s.SectorId, true),
            })
            .OrderBy(s => s.Item2)];

        var results = new (WorldSectorDocument Doc, List<WorldEntity> Entities)?[payloads.Length];
        int done = 0;
        Parallel.For(0, payloads.Length, i =>
        {
            (string path, int sectorId, bool landmark) = payloads[i];
            // TryDeserialize, because the path is a derived probe: its CRC32 can collide with a real
            // file of another format (the engine's own keyspace has such pairs), which should read
            // as "this sector has no payload", not an error.
            FcbObject? root = readByPath(path) is { } data ? FcbDocument.TryDeserialize(data) : null;
            if (root is null && everySector && !landmark)
            {
                root = ContainerSkeletons.WorldSector(sectorId);
            }
            if (root is null)
            {
                return;
            }

            var doc = new WorldSectorDocument
            {
                SourcePath = path,
                SectorId = sectorId,
                PristineRoot = root,
            };
            results[i] = (doc, ExtractEntities(doc, landmark));

            int soFar = Interlocked.Increment(ref done);
            if (soFar % 250 == 0)
            {
                progress?.Report($"Loading {map.Name}: {soFar}/{payloads.Length} sector files");
            }
        });

        // Keyed by sector, so only the worldsector file - the one a sector edit writes back - takes
        // a slot. A landmark entity still carries its own document, and with it the path to edit.
        var byId = new Dictionary<int, WorldSectorDocument>(map.Sectors.Count);
        var entities = new List<WorldEntity>(results.Sum(r => r?.Entities.Count ?? 0));
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i] is not (WorldSectorDocument doc, List<WorldEntity> docEntities))
            {
                continue;
            }

            if (!payloads[i].Landmark)
            {
                byId[doc.SectorId] = doc;
            }

            entities.AddRange(docEntities);
        }

        progress?.Report($"Loaded {map.Name}: {byId.Count} sectors, {entities.Count:N0} entities");
        return new Fc2World { Name = map.Name, SectorsById = byId, Entities = entities };
    }

    /// <summary>A few hundred distinct archetype names cover all ~90k entities; interning them keeps
    /// the pool from holding tens of thousands of identical strings.</summary>
    private static readonly ConcurrentDictionary<string, string> InternedNames = [];

    /// <summary>
    /// The entities a payload file places. A landmark file contributes only what it draws: the rest
    /// of it is the sector's vegetation container and its spline and occlusion volumes, which the
    /// vegetation and road layers already own and which would otherwise pile 15,000 markers on the
    /// sector corners. Campaign landmark objects name no archetype, so the graphics component they
    /// carry is the whole of their geometry and nothing resolvable is dropped.
    /// </summary>
    private static List<WorldEntity> ExtractEntities(WorldSectorDocument doc, bool drawnOnly)
    {
        var entities = new List<WorldEntity>();
        foreach (FcbObject layer in doc.PristineRoot.Children)
        {
            if (layer.TypeHash != WorldHashes.MissionLayer)
            {
                continue;
            }

            string pathId = Intern(FcbEntityFields.ReadString(layer, WorldHashes.TextPathId));
            foreach (FcbObject node in layer.Children)
            {
                if (node.TypeHash != WorldHashes.Entity)
                {
                    continue;
                }

                if (drawnOnly && WorldModels.MeshPaths(node).Count == 0)
                {
                    continue;
                }

                string name = FcbEntityFields.ReadString(node, WorldHashes.HidName);
                string archetype = Intern(FcbEntityFields.ReadString(node, WorldHashes.TplCreatureType));
                entities.Add(new WorldEntity
                {
                    Node = node,
                    HomeSector = doc,
                    LayerPathId = pathId,
                    Id = FcbEntityFields.ReadU64(node, WorldHashes.DisEntityId),
                    Name = name.Length > 0 ? name : archetype,
                    ArchetypeName = archetype,
                    Position = FcbEntityFields.ReadVector3(node, WorldHashes.HidPos)
                        ?? FcbEntityFields.ReadVector3(node, WorldHashes.HidPosPrecise),
                    Angles = FcbEntityFields.ReadVector3(node, WorldHashes.HidAngles) ?? default,
                });
            }
        }
        return entities;
    }

    private static string Intern(string text) => InternedNames.GetOrAdd(text, text);
}
