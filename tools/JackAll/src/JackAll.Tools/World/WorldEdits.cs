using System.Numerics;
using System.Security.Cryptography;
using JackAll.Core.Mods;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>A prefab member copied with its prefab: its node, where it stood relative to the prefab,
/// and the entity it came from, which a paste draws it like, when it came from one.</summary>
public sealed record CopiedMember(FcbObject Node, Vector3 Offset, WorldEntity? Original);

/// <summary>A placed entity lifted out of a loaded world, to be pasted into it or into another. A
/// prefab carries its members along, so the paste can give them new ids and list those instead.</summary>
public sealed record CopiedEntity(FcbObject Node, string SourceWorld)
{
    public IReadOnlyList<CopiedMember> Members { get; init; } = [];

    /// <summary>An exact clone apart from identity and placement, which a paste assigns.</summary>
    public static CopiedEntity Of(WorldEntity entity, string world) => new(entity.Node.Clone(), world);

    public string ArchetypeName => FcbEntityFields.ReadString(Node, WorldHashes.TplCreatureType);
}

/// <summary>One entity's fragment as a save stages it.</summary>
public sealed record EntityFragment(string ContainerPath, string FragmentId, FcbObject Node);

/// <summary>A placed entity removed from its sector.</summary>
public sealed record DeletedEntity(string ContainerPath, ulong Id, string Name);

/// <summary>A deleted entity and the session state it had: its edited node, whether it was pending a
/// save, and the source it was added from when it was new.</summary>
public sealed record DeletedRecord(WorldEntity Entity, FcbObject? Working, bool WasTouched, CopiedEntity? Source);

/// <summary>
/// The Map tab's unsaved edits to one loaded world: entities added, moved, field-edited and deleted.
/// Loaded entity nodes stay pristine; <see cref="Pending"/> writes each edit into a clone.
/// </summary>
public sealed class WorldEditSession(Fc2World world, int sectorsPerSide)
{
    /// <summary>Above every retail id (they start 0x1C8...), so a pasted id cannot collide with one.</summary>
    private const ulong NewIdFloor = 0x4000_0000_0000_0000;

    private readonly HashSet<WorldEntity> _touched = [];
    private readonly HashSet<WorldEntity> _deleted = [];
    private readonly Dictionary<WorldEntity, CopiedEntity> _added = [];
    private readonly HashSet<WorldEntity> _restored = [];

    /// <summary>Each field-edited entity's own copy of its node; kept past a save so a later edit
    /// builds on the saved one.</summary>
    private readonly Dictionary<WorldEntity, FcbObject> _working = [];

    /// <summary>Every live entity by id, and every name taken, kept in step with the world's list.</summary>
    private readonly Dictionary<ulong, WorldEntity> _byId = IndexById(world.Entities);
    private readonly HashSet<string> _names = [.. world.Entities.Select(e => e.Name)];

    public Fc2World World => world;

    /// <summary>The live entity with this id, or null when the world places none.</summary>
    public WorldEntity? EntityById(ulong id) => _byId.GetValueOrDefault(id);

    public bool IsDirty => _touched.Count + _deleted.Count + _added.Count > 0;

    /// <summary>Deleted since the last save.</summary>
    public IReadOnlyCollection<WorldEntity> Deleted => _deleted;

    /// <summary>Added, moved or field-edited since the last save.</summary>
    public bool IsModified(WorldEntity entity) => _added.ContainsKey(entity) || _touched.Contains(entity);

    /// <summary>Copies an entity with its edits, and a prefab with the members it lists.</summary>
    public CopiedEntity Copy(WorldEntity entity)
    {
        FcbObject node = CurrentNode(entity);
        Vector3 origin = entity.Position ?? default;
        return new CopiedEntity(node.Clone(), world.Name)
        {
            Members = [.. EntityGroups.ChildrenOf(node)
                .Select(child => EntityById(child.Id))
                .OfType<WorldEntity>()
                .Where(m => m.Position is not null)
                .Select(m => new CopiedMember(CurrentNode(m).Clone(), m.Position!.Value - origin, m))],
        };
    }

    /// <summary>
    /// Adds a clone of <paramref name="copy"/> at <paramref name="position"/>, filed under <c>main</c>
    /// in the sector file that position falls in, and its members around it. Every clone gets a new id
    /// and name, and the prefab's list and any links between them follow. Returns the root, then each
    /// member in the copy's order.
    /// </summary>
    public IReadOnlyList<WorldEntity> Paste(CopiedEntity copy, Vector3 position)
    {
        WorldSectorDocument sector = SectorAt(position) ?? throw NoSector();
        var ids = new Dictionary<ulong, ulong>();
        List<WorldEntity> pasted = [AddClone(copy, sector, position, ids)];
        foreach (CopiedMember member in copy.Members)
        {
            Vector3 at = position + member.Offset;
            pasted.Add(AddClone(new CopiedEntity(member.Node, copy.SourceWorld), SectorAt(at) ?? sector, at, ids));
        }

        if (EntityGroups.IsPrefab(pasted[0].Node))
        {
            EntityGroups.SetChildren(pasted[0].Node, pasted.Skip(1).Select(m => new PrefabChild(m.Name, m.Id)));
        }
        foreach (WorldEntity entity in pasted)
        {
            EntityLinks.Retarget(entity.Node, ids);
        }
        return pasted;
    }

    /// <summary>
    /// Groups <paramref name="members"/> under a new prefab entity at their centre, laid out as every
    /// retail prefab is: a class-bound entity listing its members by name and id. They must share one
    /// mission layer, as every retail prefab's members do.
    /// </summary>
    public WorldEntity Group(IReadOnlyList<WorldEntity> members)
    {
        List<WorldEntity> placed = [.. members.Where(m => m.Position is not null)];
        if (placed.Count == 0)
        {
            throw new InvalidOperationException("Select the entities to group first.");
        }
        if (placed.Select(m => m.LayerPathId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            throw new InvalidOperationException("A prefab's members share one mission layer, and these span several.");
        }
        if (placed.Any(m => EntityGroups.IsPrefab(CurrentNode(m))))
        {
            throw new InvalidOperationException("A prefab cannot hold another prefab; no retail one does.");
        }

        Vector3 centre = placed.Aggregate(Vector3.Zero, (sum, m) => sum + m.Position!.Value) / placed.Count;
        WorldSectorDocument sector = SectorAt(centre) ?? placed[0].HomeSector;
        (ulong id, string name) = NewIdentity("Prefab");
        FcbObject node = NewEntityNode(id, name, centre);
        node.Values[WorldHashes.EntityClassName] = FcbEntityFields.StringBytes(EntityGroups.PrefabClass);
        node.Values[WorldHashes.HidEntityClass] = BitConverter.GetBytes(FcbClassDefinitions.Crc32Ascii(EntityGroups.PrefabClass));
        node.Values[WorldHashes.HidResourceCount] = BitConverter.GetBytes(0u);
        node.Values[WorldHashes.HidConstEntity] = [0];
        EntityGroups.SetChildren(node, placed.Select(m => new PrefabChild(m.Name, m.Id)));
        return Add(new CopiedEntity(node, world.Name), node, sector, placed[0].LayerPathId, id, name, centre);
    }

    /// <summary>
    /// Adds a new instance of <paramref name="archetype"/> at <paramref name="position"/>, filed under
    /// <paramref name="layerPathId"/>: the fields every shipped archetype-bound instance carries and
    /// nothing more, so everything else is inherited. See
    /// docs/docs/engine-internals/entity-instancing.md.
    /// </summary>
    public WorldEntity Place(ArchetypeDefinition archetype, Vector3 position, string layerPathId)
    {
        WorldSectorDocument sector = SectorAt(position) ?? throw NoSector();
        int group = archetype.Name.IndexOf('.');
        (ulong id, string name) = NewIdentity(group < 0 ? archetype.Name : archetype.Name[(group + 1)..]);

        FcbObject node = NewEntityNode(id, name, position);
        node.Values[WorldHashes.TplCreatureType] = FcbEntityFields.StringBytes(archetype.Name);
        return Add(new CopiedEntity(node, world.Name), node, sector, layerPathId, id, name, position);
    }

    /// <summary>What every entity this session creates carries: identity, placement, and the event
    /// component with an empty link list.</summary>
    private static FcbObject NewEntityNode(ulong id, string name, Vector3 position)
    {
        byte[] at = FcbEntityFields.Vector3Bytes(position);
        var components = new FcbObject { TypeHash = WorldHashes.Components };
        components.Children.Add(EntityLinks.NewEventComponent());
        var node = new FcbObject { TypeHash = WorldHashes.Entity };
        node.Values[WorldHashes.HidName] = FcbEntityFields.StringBytes(name);
        node.Values[WorldHashes.DisEntityId] = BitConverter.GetBytes(id);
        node.Values[WorldHashes.HidPos] = at;
        node.Values[WorldHashes.HidAngles] = FcbEntityFields.Vector3Bytes(default);
        node.Values[WorldHashes.HidPosPrecise] = (byte[])at.Clone();
        node.Children.Add(components);
        return node;
    }

    /// <summary>Adds a clone of <paramref name="source"/>'s node under a new id and name, recording the id it
    /// replaces in <paramref name="ids"/>.</summary>
    private WorldEntity AddClone(CopiedEntity source, WorldSectorDocument sector, Vector3 position, Dictionary<ulong, ulong> ids)
    {
        (ulong id, string name) = NewIdentity(FcbEntityFields.ReadString(source.Node, WorldHashes.HidName));
        FcbObject clone = source.Node.Clone();
        ids[FcbEntityFields.ReadU64(source.Node, WorldHashes.DisEntityId)] = id;
        clone.Values[WorldHashes.DisEntityId] = BitConverter.GetBytes(id);
        clone.Values[WorldHashes.HidName] = FcbEntityFields.StringBytes(name);
        return Add(source, clone, sector, MissionLayers.MainName, id, name, position);
    }

    /// <summary>An unsaved paste follows its position into whichever sector file it lands in; any other
    /// entity stays filed in the sector it was loaded from.</summary>
    public void Moved(WorldEntity entity)
    {
        if (!_added.ContainsKey(entity))
        {
            _touched.Add(entity);
        }
        else if (SectorAt(entity.Position!.Value) is { } sector)
        {
            entity.HomeSector = sector;
        }
    }

    /// <summary>The node field edits go into: a new entity's own, else a copy of the loaded one that
    /// leaves the sector's pristine tree alone. Nothing is pending until <see cref="Edited"/>.</summary>
    public FcbObject EditableNode(WorldEntity entity)
    {
        if (entity.IsNew)
        {
            return entity.Node;
        }
        if (!_working.TryGetValue(entity, out FcbObject? node))
        {
            _working[entity] = node = entity.Node.Clone();
        }
        return node;
    }

    /// <summary>The entity's node with its unsaved field edits, without starting a copy of it.</summary>
    public FcbObject CurrentNode(WorldEntity entity) => _working.GetValueOrDefault(entity) ?? entity.Node;

    /// <summary>Records a change made through <see cref="EditableNode"/>.</summary>
    public void Edited(WorldEntity entity)
    {
        if (!_added.ContainsKey(entity))
        {
            _touched.Add(entity);
        }
    }

    /// <summary>Removes the entity, returning what <see cref="Restore"/> needs to put it back.</summary>
    public DeletedRecord Delete(WorldEntity entity)
    {
        var record = new DeletedRecord(
            entity, _working.GetValueOrDefault(entity), _touched.Contains(entity), _added.GetValueOrDefault(entity));
        Untrack(entity);
        _touched.Remove(entity);
        _working.Remove(entity);
        _restored.Remove(entity);
        if (!_added.Remove(entity))
        {
            _deleted.Add(entity);
        }
        return record;
    }

    /// <summary>Puts a deleted entity back as it was. One whose delete was already saved is restaged,
    /// and its id listed in <see cref="Restored"/> so the save takes the staged delete back out.</summary>
    public void Restore(DeletedRecord record)
    {
        WorldEntity entity = record.Entity;
        Track(entity);
        if (record.Working is { } working)
        {
            _working[entity] = working;
        }
        if (record.Source is { } source)
        {
            _added[entity] = source;
            return;
        }
        bool deleteWasSaved = !_deleted.Remove(entity);
        if (deleteWasSaved)
        {
            _restored.Add(entity);
        }
        if (deleteWasSaved || record.WasTouched)
        {
            _touched.Add(entity);
        }
    }

    /// <summary>Entities whose saved delete has been undone since the last save.</summary>
    public IEnumerable<DeletedEntity> Restored
        => _restored.Select(e => new DeletedEntity(e.HomeSector.SourcePath, e.Id, e.Name));

    /// <summary>Every source a pending addition was built from.</summary>
    public IEnumerable<CopiedEntity> Additions => _added.Values;

    /// <summary>
    /// The mission layers pending additions outside <c>main</c> must be filed under, per sector file.
    /// A fragment carries no layer, so without these a new entity lands in <c>main</c>.
    /// </summary>
    public IReadOnlyList<(string ContainerPath, LayerSpec Layer)> LayerPlacements()
        => [.. _added.Keys
            .Where(e => !MissionLayers.IsMain(e.LayerPathId))
            .GroupBy(e => (e.HomeSector.SourcePath, e.LayerPathId))
            .Select(g => (g.Key.SourcePath, new LayerSpec(
                g.Key.LayerPathId, PathIdOf(g.Key.LayerPathId), Before: null, Values: [],
                Entities: [.. g.Select(e => e.Id)])))];

    /// <summary>The fragments to stage and the entities to delete, per sector file.</summary>
    public (IReadOnlyList<EntityFragment> Fragments, IReadOnlyList<DeletedEntity> Deleted) Pending()
    {
        List<EntityFragment> fragments = [.. _added.Keys.Concat(_touched).Select(entity =>
            new EntityFragment(
                entity.HomeSector.SourcePath,
                FcbFragments.EntityFragmentId(FcbEntityFields.ReadString(entity.Node, WorldHashes.HidName), entity.Id),
                Placed(entity)))];
        List<DeletedEntity> deleted = [.. _deleted.Select(e => new DeletedEntity(e.HomeSector.SourcePath, e.Id, e.Name))];
        return (fragments, deleted);
    }

    /// <summary>Forgets the edits once staged. A saved paste is an entity like any other from here on,
    /// so moving it again restages its fragment and deleting it goes through the workspace.</summary>
    public void Saved()
    {
        foreach (WorldEntity entity in _added.Keys)
        {
            entity.IsNew = false;
        }
        _added.Clear();
        _touched.Clear();
        _deleted.Clear();
        _restored.Clear();
    }

    /// <summary>The entity's node with its edits and placement written in; the node itself is left
    /// alone. An entity with no angles of its own gets them only once it is actually turned.</summary>
    private FcbObject Placed(WorldEntity entity)
    {
        FcbObject node = CurrentNode(entity).Clone();
        byte[] position = FcbEntityFields.Vector3Bytes(entity.Position!.Value);
        node.Values[WorldHashes.HidPos] = position;
        if (node.Values.ContainsKey(WorldHashes.HidPosPrecise))
        {
            node.Values[WorldHashes.HidPosPrecise] = (byte[])position.Clone();
        }
        if (node.Values.ContainsKey(WorldHashes.HidAngles) || entity.Angles != default)
        {
            node.Values[WorldHashes.HidAngles] = FcbEntityFields.Vector3Bytes(entity.Angles);
        }
        return node;
    }

    private WorldEntity Add(
        CopiedEntity source, FcbObject node, WorldSectorDocument sector, string layerPathId, ulong id, string name,
        Vector3 position)
    {
        var entity = new WorldEntity
        {
            Node = node,
            HomeSector = sector,
            LayerPathId = layerPathId,
            Id = id,
            Name = name,
            ArchetypeName = source.ArchetypeName,
            Position = position,
            Angles = FcbEntityFields.ReadVector3(node, WorldHashes.HidAngles) ?? default,
            IsNew = true,
        };
        Track(entity);
        _added[entity] = source;
        return entity;
    }

    private static InvalidOperationException NoSector()
        => new("There is no sector file at that spot to place an entity in.");

    /// <summary>The id a layer is stored with in whichever sector already has it, since a shipped id is
    /// not always the hash of its path.</summary>
    private uint PathIdOf(string path)
    {
        foreach (WorldSectorDocument sector in world.SectorsById.Values)
        {
            foreach (FcbObject layer in FcbFragments.LayersOf(sector.PristineRoot))
            {
                if (MissionLayers.NameOf(layer).Equals(path, StringComparison.OrdinalIgnoreCase)
                    && MissionLayers.PathIdOf(layer) is { } id)
                {
                    return id;
                }
            }
        }
        return NameHash.Compute(path);
    }

    private WorldSectorDocument? SectorAt(Vector3 position)
        => SectorIdAt(position) is { } id ? world.SectorsById.GetValueOrDefault(id) : null;

    /// <summary>The id of the sector a position falls in, or null off the map.</summary>
    public int? SectorIdAt(Vector3 position)
    {
        (int x, int y) = WorldModels.SectorOf(position);
        return x >= 0 && y >= 0 && x < sectorsPerSide && y < sectorsPerSide ? y * sectorsPerSide + x : null;
    }

    /// <summary>An id no loaded entity has, and <paramref name="stem"/> numbered past every name taken.</summary>
    private (ulong Id, string Name) NewIdentity(string stem)
    {
        ulong id;
        do
        {
            id = NewIdFloor | (BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) & (NewIdFloor - 1));
        }
        while (_byId.ContainsKey(id));

        int n = 1;
        while (_names.Contains($"{stem}_{n}"))
        {
            n++;
        }
        return (id, $"{stem}_{n}");
    }

    private void Track(WorldEntity entity)
    {
        world.Entities.Add(entity);
        _byId[entity.Id] = entity;
        _names.Add(entity.Name);
    }

    /// <summary>Its name stays taken, since another entity may share it.</summary>
    private void Untrack(WorldEntity entity)
    {
        world.Entities.Remove(entity);
        if (_byId.GetValueOrDefault(entity.Id) == entity)
        {
            _byId.Remove(entity.Id);
        }
    }

    private static Dictionary<ulong, WorldEntity> IndexById(IEnumerable<WorldEntity> entities)
    {
        var byId = new Dictionary<ulong, WorldEntity>();
        foreach (WorldEntity entity in entities)
        {
            byId.TryAdd(entity.Id, entity);
        }
        return byId;
    }
}

/// <summary>What a pasted entity needs from files besides its own sector.</summary>
public static class WorldEditDependencies
{
    public static string DepLoadPathOf(string world) => $@"worlds\{world}\generated\{world}{ContainerFormats.DepLoadSuffix}";

    /// <summary>The library single-player reads for <paramref name="world"/> - see
    /// docs/docs/engine-internals/entity-instancing.md.</summary>
    public static string LibraryPathOf(string world) => ArchetypeIndex.BaseLayer(world).Path;

    /// <summary>What an entity draws: its own meshes, else its archetype's.</summary>
    public static IReadOnlyList<string> MeshesOf(FcbObject node, FcbObject? archetype)
    {
        IReadOnlyList<string> own = WorldModels.MeshPaths(node);
        return own.Count > 0 || archetype is null ? own : WorldModels.MeshPaths(archetype);
    }

    /// <summary>
    /// The resources <paramref name="meshPaths"/> need listed in <paramref name="world"/>'s depload,
    /// copied from whichever shipped depload lists them: each mesh, then its materials. Textures are
    /// leaves and come along as children. Meshes no depload lists are returned as unresolved.
    /// </summary>
    public static (IReadOnlyList<DepLoadParent> Additions, IReadOnlyList<string> Unresolved) DepLoadAdditions(
        string world, IEnumerable<string> meshPaths, Func<string, byte[]?> readByPath, IEnumerable<string> knownPaths)
    {
        string targetPath = DepLoadPathOf(world);
        byte[] target = readByPath(targetPath)
            ?? throw new InvalidOperationException($"'{targetPath}' is not in the merged filesystem.");
        HashSet<uint> listed = [.. DepLoadDocument.Decode(target).Parents.Select(p => p.Hash)];

        List<string> missing = [.. meshPaths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !listed.Contains(NameHash.Compute(path)))];
        if (missing.Count == 0)
        {
            return ([], []);
        }

        var donors = new Dictionary<uint, DepLoadParent>();
        foreach (string path in knownPaths.Where(p =>
            ContainerFormats.IsDepLoad(p)
            && !p.Equals(targetPath, StringComparison.OrdinalIgnoreCase)))
        {
            if (readByPath(path) is not { } bytes)
            {
                continue;
            }
            foreach (DepLoadParent parent in DepLoadDocument.Decode(bytes).Parents)
            {
                donors.TryAdd(parent.Hash, parent);
            }
        }

        var additions = new List<DepLoadParent>();
        var unresolved = new List<string>();
        foreach (string mesh in missing)
        {
            if (!donors.TryGetValue(NameHash.Compute(mesh), out DepLoadParent? parent))
            {
                unresolved.Add(mesh);
                continue;
            }

            var pending = new Queue<DepLoadParent>([parent]);
            while (pending.TryDequeue(out DepLoadParent? next))
            {
                if (!listed.Add(next.Hash))
                {
                    continue;
                }
                additions.Add(next);
                foreach (DepLoadChild child in next.Children)
                {
                    if (!listed.Contains(child.Hash) && donors.TryGetValue(child.Hash, out DepLoadParent? nested))
                    {
                        pending.Enqueue(nested);
                    }
                }
            }
        }
        return (additions, unresolved);
    }
}
