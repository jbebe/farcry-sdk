using System.Collections.Concurrent;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// Builds the in-memory entity model of a map from its <c>worldsector*.data.fcb</c> files, read
/// straight out of the merged filesystem - no extraction.
/// </summary>
public static class WorldLoader
{
    public static Fc2World Load(
        TerrainMap map,
        Func<string, byte[]?> readByPath,
        IProgress<string>? progress = null)
    {
        // The sector payload sits beside the terrain, one folder over - the same derivation the
        // descriptor loader uses, so the level folder never has to be recomputed.
        (string Path, int SectorId)[] sectors = [.. map.Sectors
            .Select(s => (Path: s.Path
                .Replace(@"\sdat\", @"\worldsectors\", StringComparison.OrdinalIgnoreCase)
                .Replace($"sd{s.SectorId}.sdat", $"worldsector{s.SectorId}.data.fcb", StringComparison.OrdinalIgnoreCase),
                s.SectorId))
            .OrderBy(s => s.SectorId)];

        var results = new (WorldSectorDocument Doc, List<WorldEntity> Entities)?[sectors.Length];
        int done = 0;
        Parallel.For(0, sectors.Length, i =>
        {
            (string path, int sectorId) = sectors[i];
            if (readByPath(path) is not { } data)
            {
                return;
            }

            FcbObject root;
            try
            {
                root = FcbDocument.Deserialize(data);
            }
            catch (InvalidDataException)
            {
                return;
            }

            var doc = new WorldSectorDocument
            {
                SourcePath = path,
                SectorId = sectorId,
                PristineRoot = root,
            };
            results[i] = (doc, ExtractEntities(doc));

            int soFar = Interlocked.Increment(ref done);
            if (soFar % 250 == 0)
            {
                progress?.Report($"Loading {map.Name}: {soFar}/{sectors.Length} sectors");
            }
        });

        var byId = new Dictionary<int, WorldSectorDocument>(sectors.Length);
        var entities = new List<WorldEntity>(results.Sum(r => r?.Entities.Count ?? 0));
        foreach ((WorldSectorDocument doc, List<WorldEntity> docEntities) in
            results.OfType<(WorldSectorDocument, List<WorldEntity>)>())
        {
            byId[doc.SectorId] = doc;
            entities.AddRange(docEntities);
        }

        progress?.Report($"Loaded {map.Name}: {byId.Count} sectors, {entities.Count:N0} entities");
        return new Fc2World { Name = map.Name, SectorsById = byId, Entities = entities };
    }

    /// <summary>A few hundred distinct archetype names cover all ~90k entities; interning them keeps
    /// the pool from holding tens of thousands of identical strings.</summary>
    private static readonly ConcurrentDictionary<string, string> InternedNames = [];

    private static List<WorldEntity> ExtractEntities(WorldSectorDocument doc)
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
