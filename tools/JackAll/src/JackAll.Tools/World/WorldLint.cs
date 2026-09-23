using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

public enum LintSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>One problem with one placed entity.</summary>
public sealed record WorldFinding(LintSeverity Severity, string Kind, WorldEntity Entity, string Message);

/// <summary>
/// Checks a loaded world, edits included, for what fails silently in game: an entity that spawns
/// nothing, a character that crashes a world with no navmesh, a link or prefab member aimed at
/// nothing.
/// </summary>
/// <remarks>
/// Retail worlds already hold thousands of prefab members and dozens of links that resolve nowhere,
/// and entities standing outside their sector, and load fine. So those checks look only at what the
/// session changed: an edited entity, or a target it deleted.
/// </remarks>
public static class WorldLint
{
    private static readonly uint Pawn = FcbClassDefinitions.Crc32Ascii("CPawn");

    /// <param name="hasNavMesh">Whether a sector id ships a navmesh.</param>
    /// <param name="unresolvedMeshes">Of the meshes additions draw, those no depload can supply.</param>
    public static IReadOnlyList<WorldFinding> Run(
        WorldEditSession session, ArchetypeIndex archetypes, Func<int, bool> hasNavMesh,
        Func<IEnumerable<string>, IReadOnlySet<string>> unresolvedMeshes)
    {
        var findings = new List<WorldFinding>();
        HashSet<ulong> deleted = [.. session.Deleted.Select(e => e.Id)];
        foreach (WorldEntity entity in session.World.Entities)
        {
            FcbObject node = session.CurrentNode(entity);
            bool edited = session.IsModified(entity);
            bool Broken(ulong target) => session.EntityById(target) is null && (edited || deleted.Contains(target));
            FcbObject? archetype = null;
            if (entity.ArchetypeName.Length > 0 && (archetype = archetypes.Winner(entity.ArchetypeName)?.Node) is null)
            {
                findings.Add(new(LintSeverity.Error, "Unknown archetype", entity,
                    $"No library of this world declares {entity.ArchetypeName}, so it spawns nothing."));
            }

            if (entity.Position is { } position && session.SectorIdAt(position) is { } sector)
            {
                if (edited && sector != entity.HomeSector.SectorId)
                {
                    findings.Add(new(LintSeverity.Warning, "Wrong sector", entity,
                        $"Stands in sector {sector} but is filed in sector {entity.HomeSector.SectorId}, which streams it."));
                }
                if (!hasNavMesh(sector) && (Has(node, Pawn) || (archetype is not null && Has(archetype, Pawn))))
                {
                    findings.Add(new(LintSeverity.Error, "Character without navmesh", entity,
                        $"Sector {sector} has no navmesh; a character there crashes the game about 30 s after load."));
                }
            }

            foreach (EntityLink link in EntityLinks.Read(node).Where(l => Broken(l.TargetId)))
            {
                findings.Add(new(LintSeverity.Warning, "Broken link", entity,
                    $"Its {link.Output} link sends {link.EventName} to entity {link.TargetId}, which this world does not place."));
            }
            foreach (PrefabChild child in EntityGroups.ChildrenOf(node).Where(c => Broken(c.Id)))
            {
                findings.Add(new(LintSeverity.Warning, "Missing prefab member", entity,
                    $"Lists {child.Name} ({child.Id}) as a member, which this world does not place."));
            }
        }

        findings.AddRange(MissingMeshes(session, archetypes, unresolvedMeshes));
        return findings;
    }

    private static IEnumerable<WorldFinding> MissingMeshes(
        WorldEditSession session, ArchetypeIndex archetypes, Func<IEnumerable<string>, IReadOnlySet<string>> unresolvedMeshes)
    {
        Dictionary<WorldEntity, IReadOnlyList<string>> meshes = session.World.Entities
            .Where(e => e.IsNew)
            .ToDictionary(e => e, e => WorldEditDependencies.MeshesOf(session.CurrentNode(e), archetypes.Winner(e.ArchetypeName)?.Node));
        IReadOnlySet<string> unresolved = unresolvedMeshes(meshes.Values.SelectMany(m => m));
        return meshes
            .Where(m => m.Value.Any(unresolved.Contains))
            .Select(m => new WorldFinding(LintSeverity.Warning, "Mesh not loaded", m.Key,
                $"No depload lists {m.Value.First(unresolved.Contains)}, so it may not draw; register it with 'jackall-cli depload add'."));
    }

    private static bool Has(FcbObject node, uint component) => FcbEntityFields.FindComponent(node, component) is not null;
}
