using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;
using JackAll.Tools.World;

namespace JackAll.App;

/// <summary>The Map tab's save: a world's pending edits staged into the workspace.</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// Stages every pending addition, move, edit and delete of <paramref name="session"/>, plus what an
    /// addition needs beyond its sector: its archetype in this world's library and its meshes in this
    /// world's depload. Returns the number of files staged and one line per file or problem.
    /// </summary>
    public async Task<(int Staged, IReadOnlyList<string> Report)> SaveWorldEdits(WorldEditSession session)
    {
        if (Workspace is not { } workspace || _vfs is not { } vfs)
        {
            throw new InvalidOperationException("No game install is loaded.");
        }

        List<string> paths = [.. AllKnownPaths];
        var progress = new Progress<string>(s => Status = s);
        var report = new List<string>();
        int staged = 0;
        void Stage(string path, string xml)
        {
            workspace.Stage(FolderModLayer.StorageKeyOf(path), path, "xml", AppText.EncodeUtf8(xml));
            report.Add($"staged {path}");
            staged++;
        }

        await Task.Run(() =>
        {
            FcbClassDefinitions definitions = FcbDefinitionsProvider.Value.Value;
            (IReadOnlyList<EntityFragment> fragments, IReadOnlyList<DeletedEntity> deleted) = session.Pending();
            foreach (EntityFragment fragment in fragments)
            {
                Stage($@"{fragment.ContainerPath}\{fragment.FragmentId}", FcbXml.ToXml(fragment.Node, definitions));
            }

            ILookup<string, DeletedEntity> deletes = deleted.ToLookup(d => d.ContainerPath, StringComparer.OrdinalIgnoreCase);
            ILookup<string, LayerSpec> placements = session.LayerPlacements()
                .ToLookup(p => p.ContainerPath, p => p.Layer, StringComparer.OrdinalIgnoreCase);
            ILookup<string, DeletedEntity> restored = session.Restored.ToLookup(d => d.ContainerPath, StringComparer.OrdinalIgnoreCase);
            foreach (string container in deletes.Select(g => g.Key)
                .Union(placements.Select(g => g.Key), StringComparer.OrdinalIgnoreCase)
                .Union(restored.Select(g => g.Key), StringComparer.OrdinalIgnoreCase))
            {
                StageLayout(workspace, vfs, container, [.. deletes[container]], [.. placements[container]],
                    [.. restored[container]], Stage, report);
            }

            string world = session.World.Name;
            List<CopiedEntity> added = [.. session.Additions];
            if (added.Count == 0)
            {
                return;
            }

            ArchetypeIndex IndexOf(string name) => ArchetypesOf(name, progress: progress).GetAwaiter().GetResult();

            var archetypes = new Dictionary<string, FcbObject?>(StringComparer.OrdinalIgnoreCase);
            FcbObject? ArchetypeOf(CopiedEntity paste)
            {
                if (!archetypes.TryGetValue(paste.ArchetypeName, out FcbObject? node))
                {
                    node = IndexOf(world).Winner(paste.ArchetypeName)?.Node
                        ?? StageArchetype(paste, world, IndexOf(paste.SourceWorld), Stage, report);
                    archetypes[paste.ArchetypeName] = node;
                }
                return node;
            }

            var meshes = new List<string>();
            foreach (CopiedEntity paste in added)
            {
                meshes.AddRange(WorldEditDependencies.MeshesOf(
                    paste.Node, paste.ArchetypeName.Length > 0 ? ArchetypeOf(paste) : null));
            }

            (IReadOnlyList<DepLoadParent> additions, IReadOnlyList<string> unresolved) =
                WorldEditDependencies.DepLoadAdditions(world, meshes, ReadByPath, paths);
            string depload = WorldEditDependencies.DepLoadPathOf(world);
            foreach (DepLoadParent parent in additions)
            {
                Stage($@"{depload}\{DepLoadContainerSplitter.IdOf(parent.Hash)}", DepLoadXml.FragmentToXml(parent, _names));
            }
            report.AddRange(unresolved.Select(mesh =>
                $"{mesh} is in no shipped depload - register it with 'jackall-cli depload add'"));
        });

        session.Saved();
        Reindex();
        return (staged, report);
    }

    /// <summary>Runs <see cref="WorldLint"/> over <paramref name="session"/> against the merged filesystem.</summary>
    public Task<IReadOnlyList<WorldFinding>> CheckWorld(WorldEditSession session, TerrainMap map, ArchetypeIndex archetypes)
        => Task.Run(() =>
        {
            List<string> paths = [.. AllKnownPaths];
            HashSet<string> known = new(paths, StringComparer.OrdinalIgnoreCase);
            Dictionary<int, string> sdat = map.Sectors.ToDictionary(s => s.SectorId, s => s.Path);
            return WorldLint.Run(
                session, archetypes,
                sector => sdat.TryGetValue(sector, out string? path) && known.Contains(WorldNavMesh.PathOf(path, sector)),
                meshes => known.Contains(WorldEditDependencies.DepLoadPathOf(session.World.Name))
                    ? WorldEditDependencies.DepLoadAdditions(session.World.Name, meshes, ReadByPath, paths).Unresolved.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>());
        });

    /// <summary>
    /// What one sector file's staged <c>_layout.xml</c> must say: the entities deleted from it and the
    /// mission layers new entities are filed under. An entity the workspace itself added is simply
    /// unstaged rather than deleted, and a restored one has its earlier staged delete taken out.
    /// </summary>
    private static void StageLayout(
        FolderModLayer workspace, Core.Vfs.GameVfs vfs, string containerPath, IReadOnlyList<DeletedEntity> deleted,
        IReadOnlyList<LayerSpec> placements, IReadOnlyList<DeletedEntity> restored, Action<string, string> stage,
        List<string> report)
    {
        uint containerHash = NameHash.Compute(containerPath);
        IReadOnlyList<FragmentOverride> staged =
            workspace.FragmentOverrides.GetValueOrDefault(containerHash) ?? [];
        HashSet<string> retail = vfs.ReadOriginal(containerHash) is { } original
            ? [.. FcbFragments.List(FcbDocument.Deserialize(original)).Select(f => FcbFragments.Canonicalize(f.Id))]
            : [];

        var retailDeletes = new List<string>();
        foreach (DeletedEntity entity in deleted)
        {
            string id = FcbFragments.EntityFragmentId(entity.Id);
            FragmentOverride own = staged.FirstOrDefault(f => FcbFragments.IdComparer.Equals(f.FragmentId, id));
            if (own.FragmentId is not null)
            {
                workspace.Unstage(own.EntryHash);
                report.Add($"unstaged {entity.Name}");
            }

            if (retail.Contains(id))
            {
                retailDeletes.Add(id);
            }
            else if (own.FragmentId is null)
            {
                report.Add($"{entity.Name} was added by another mod - disable that mod to remove it");
            }
        }

        FragmentOverride layout = staged.FirstOrDefault(f => ContainerLayout.IsLayoutId(f.FragmentId));
        if (retailDeletes.Count == 0 && placements.Count == 0 && (restored.Count == 0 || layout.FragmentId is null))
        {
            return;
        }

        ContainerLayout existing = layout.FragmentId is null
            ? new ContainerLayout([])
            : ContainerLayout.Parse(AppText.DecodeUtf8(workspace.Read(layout.EntryHash)));
        HashSet<string> undeleted = new(restored.Select(r => FcbFragments.EntityFragmentId(r.Id)), FcbFragments.IdComparer);
        existing = new ContainerLayout(existing.Layers, existing.Removed, [.. existing.Deleted.Where(id => !undeleted.Contains(id))]);
        (ContainerLayout merged, bool conflict) = ContainerLayout.Merge(
            new ContainerLayout([]), existing, new ContainerLayout(placements, deleted: retailDeletes));
        stage($@"{containerPath}\{ContainerLayout.Id}", merged.Render());
        if (conflict)
        {
            report.Add($"{containerPath}: the staged layout files some deleted entities into a layer, so they stay");
        }
    }

    /// <summary>
    /// Copies a pasted entity's archetype from its source world's library into this world's, where
    /// this world does not declare it. Returns the archetype's entity node, or null when the source
    /// world does not declare it either.
    /// </summary>
    private FcbObject? StageArchetype(
        CopiedEntity paste, string world, ArchetypeIndex source, Action<string, string> stage, List<string> report)
    {
        if (source.Winner(paste.ArchetypeName) is not { FragmentId: { } fragmentId } definition
            || FindFragment(definition.ContainerHash, fragmentId) is not { } row)
        {
            report.Add($"archetype {paste.ArchetypeName} is not in {paste.SourceWorld}'s library either - the paste will not spawn");
            return null;
        }

        stage($@"{WorldEditDependencies.LibraryPathOf(world)}\{fragmentId}", AppText.DecodeUtf8(Read(row)));
        return definition.Node;
    }
}
