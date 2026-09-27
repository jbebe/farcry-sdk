using JackAll.App.FileHandlers.Fcb;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;
using JackAll.Tools.Ai;
using JackAll.Tools.World;

namespace JackAll.App.Ai;

/// <summary>The copies of some archetypes in every single-player world, and the libraries they came from.</summary>
public sealed record AiLibrarySet(IReadOnlyList<TuningCopy> Copies, IReadOnlyDictionary<uint, VfsFile> Containers, IReadOnlyList<string> Worlds)
{
    public static AiLibrarySet Empty { get; } = new([], new Dictionary<uint, VfsFile>(), []);

    /// <summary>The winning declaration of every archetype <paramref name="covers"/> accepts, in each single-player world.</summary>
    public static async Task<AiLibrarySet> LoadAsync(MainViewModel vm, Func<FcbObject, bool> covers, IProgress<string> progress)
    {
        List<string> worlds = [.. ArchetypeIndex.DiscoverWorlds(vm.AllKnownPaths).Where(w => w.StartsWith("world", StringComparison.OrdinalIgnoreCase))];
        List<TuningCopy> copies = [];
        var containers = new Dictionary<uint, VfsFile>();

        foreach (string world in worlds)
        {
            progress.Report($"Reading {world}'s archetypes…");
            ArchetypeIndex index = await vm.ArchetypesOf(world, progress);
            foreach (IGrouping<uint, ArchetypeDefinition> library in index.Names
                         .Select(index.Winner).OfType<ArchetypeDefinition>()
                         .Where(d => covers(d.Node))
                         .GroupBy(d => d.ContainerHash))
            {
                if (vm.FindByHash(library.Key) is { } container)
                {
                    containers[library.Key] = container;
                    copies.AddRange(await Task.Run(() => TuningLibrary.Open(library, vm.Read(container), vm.ReadOriginal(container))));
                }
            }
        }
        return new AiLibrarySet(copies, containers, worlds);
    }

    /// <summary>Stages <paramref name="copies"/>' fragments; a copy back to vanilla is unstaged.</summary>
    public async Task StageAsync(MainViewModel vm, IEnumerable<TuningCopy> copies)
    {
        FcbClassDefinitions definitions = FcbDefinitionsProvider.Value.Value;
        var plans = await Task.Run(() => copies
            .GroupBy(c => c.Definition.ContainerHash)
            .Select(g => (Container: Containers[g.Key], Fragments: (IEnumerable<(string, string, bool)>)[.. g.Select(c => c.Plan(definitions))]))
            .ToList());
        vm.StageFragments(plans);
    }
}
