using JackAll.Core.Format;
using JackAll.Core.Mods;

namespace JackAll.Tools.World;

/// <summary>One fragment a mod stages, identified the way <see cref="ArchetypeIndex"/> attributes a
/// declaration.</summary>
public readonly record struct StagedFragment(string Source, uint ContainerHash, string FragmentId);

/// <summary>
/// A staged edit to an archetype some later library declares again. The file changes, the game reads
/// the other copy.
/// </summary>
public sealed record DeadEdit(
    string Source, string Archetype, string EditedPath, string? FragmentId, string WinningPath);

/// <summary>
/// Checks staged fragment edits against the override chain, so a mod that edits a shadowed archetype
/// is caught before it ships rather than after someone reports it doing nothing.
/// </summary>
public static class ArchetypeLint
{
    /// <summary>Every fragment the enabled layers stage, tagged with the layer that staged it.</summary>
    public static IEnumerable<StagedFragment> StagedFragmentsOf(IEnumerable<IModLayer> layers)
        => from layer in layers
           where layer.Enabled
           from container in layer.FragmentOverrides
           from fragment in container.Value
           select new StagedFragment(layer.Name, container.Key, fragment.FragmentId);

    /// <summary>
    /// Every staged edit that lands on a shadowed declaration. Only the chains an edit actually
    /// touches get resolved: a world library can only be shadowed inside its own world's chain, and
    /// everything above the base is world-independent, so one extra pass covers the rest.
    /// </summary>
    public static IReadOnlyList<DeadEdit> Run(
        IEnumerable<StagedFragment> staged, IEnumerable<string> knownPaths,
        Func<string, byte[]?> readByPath, LibraryProfile profile = LibraryProfile.Client,
        IProgress<string>? progress = null)
    {
        List<StagedFragment> edits = [.. staged];
        if (edits.Count == 0)
        {
            return [];
        }

        List<string> paths = [.. knownPaths];
        IReadOnlyList<string> dlc = ArchetypeIndex.DiscoverDlcLibraries(paths);
        IReadOnlyList<ArchetypeLayer> shared = ArchetypeIndex.SharedLayers(dlc);
        var found = new HashSet<DeadEdit>();

        foreach (string world in ArchetypeIndex.DiscoverWorlds(paths))
        {
            ArchetypeLayer libraryBase = ArchetypeIndex.BaseLayer(world, profile);
            uint baseHash = NameHash.Compute(libraryBase.Path);
            List<StagedFragment> touching = [.. edits.Where(e => e.ContainerHash == baseHash)];
            if (touching.Count == 0)
            {
                continue;
            }

            progress?.Report($"Checking {world}'s archetype chain");
            Collect(ArchetypeIndex.Load([libraryBase, .. shared], readByPath), touching, found);
        }

        var sharedHashes = shared.Select(l => NameHash.Compute(l.Path)).ToHashSet();
        List<StagedFragment> sharedEdits = [.. edits.Where(e => sharedHashes.Contains(e.ContainerHash))];
        if (sharedEdits.Count > 0)
        {
            progress?.Report("Checking the patch override and DLC libraries");
            Collect(ArchetypeIndex.Load(shared, readByPath), sharedEdits, found);
        }
        return [.. found];
    }

    private static void Collect(
        ArchetypeIndex index, IEnumerable<StagedFragment> edits, HashSet<DeadEdit> into)
    {
        foreach (StagedFragment edit in edits)
        {
            foreach (ArchetypeDefinition dead in index.DeadDeclarationsIn(edit.ContainerHash, edit.FragmentId))
            {
                into.Add(new DeadEdit(
                    edit.Source, dead.Name, dead.Layer.Path, dead.FragmentId,
                    index.Winner(dead.Name)!.Layer.Path));
            }
        }
    }
}
