using JackAll.App.FileHandlers.Fcb.FcbEditor;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Vfs;
using JackAll.Tools.World;

namespace JackAll.App;

/// <summary>Per-world indexes shared by every tab that merges an entity over something: archetypes,
/// and placed entities by id. Each loads once, until the next reindex.</summary>
public sealed partial class MainViewModel
{
    private readonly Dictionary<string, Task<ArchetypeIndex>> _archetypeIndexes = [];
    private readonly Dictionary<string, Task<IReadOnlyDictionary<ulong, WorldEntity>>> _placedIndexes = [];

    /// <summary>The archetypes <paramref name="world"/> resolves against, as the merged filesystem stands now.</summary>
    public Task<ArchetypeIndex> ArchetypesOf(string world, IProgress<string>? progress = null)
        => Cached(_archetypeIndexes, world.ToLowerInvariant(),
            () => ArchetypeIndex.Load(world, ReadByPath, progress, ArchetypeIndex.DiscoverDlcLibraries(AllKnownPaths)));

    /// <summary>Every entity placed in <paramref name="world"/>'s sectors, by id; empty for an unknown world.</summary>
    public Task<IReadOnlyDictionary<ulong, WorldEntity>> PlacedEntitiesOf(string world)
        => Cached<string, IReadOnlyDictionary<ulong, WorldEntity>>(_placedIndexes, world.ToLowerInvariant(), () =>
        {
            TerrainMap? map = TerrainMap.Discover(AllKnownPaths)
                .FirstOrDefault(m => m.Name.Equals(world, StringComparison.OrdinalIgnoreCase));
            var byId = new Dictionary<ulong, WorldEntity>();
            foreach (WorldEntity entity in map is null ? [] : WorldLoader.Load(map, ReadByPath).Entities)
            {
                byId.TryAdd(entity.Id, entity);
            }
            return byId;
        });

    /// <summary>A blocking archetype lookup for <paramref name="world"/>, for work already off the UI thread.</summary>
    public Func<string, FcbObject?> ArchetypeLookup(string world)
        => name => ArchetypesOf(world).GetAwaiter().GetResult().Winner(name)?.Node;

    /// <summary>A fragment as an editable document, compared against its vanilla content.</summary>
    public FcbDocumentViewModel OpenFragmentDocument(VfsFile fragment)
        => new(
            fragment.FileName,
            FcbXml.FromXml(AppText.DecodeUtf8(Read(fragment))),
            ReadOriginalFragment(fragment) is { } original ? FcbXml.FromXml(original) : null,
            new FcbEditContext(),
            StageFragmentEdits(fragment),
            EntitiesUnder(fragment.Path));

    /// <summary>A whole container as an editable document, staged back as binary.</summary>
    public FcbDocumentViewModel OpenContainerDocument(VfsFile container)
        => new(
            container.FileName,
            FcbDocument.Deserialize(Read(container)),
            ReadOriginal(container) is { } original ? FcbDocument.Deserialize(original) : null,
            new FcbEditContext(),
            StageContainerEdits(container),
            EntitiesUnder(container.Path));

    private ArchetypeBases EntitiesUnder(string path)
        => new(TerrainMap.MapOfPath(path) is { } world ? ArchetypeLookup(world) : null);

    private static Task<TValue> Cached<TKey, TValue>(Dictionary<TKey, Task<TValue>> cache, TKey key, Func<TValue> load)
        where TKey : notnull
    {
        lock (cache)
        {
            if (!cache.TryGetValue(key, out Task<TValue>? task) || task.IsFaulted)
            {
                cache[key] = task = Task.Run(load);
            }
            return task;
        }
    }

    private void ForgetWorldIndexes()
    {
        lock (_archetypeIndexes)
        {
            _archetypeIndexes.Clear();
        }
        lock (_placedIndexes)
        {
            _placedIndexes.Clear();
        }
    }
}
