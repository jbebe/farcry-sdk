using System.Numerics;
using System.Security.Cryptography;
using JackAll.Core.Mods;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>A placed entity lifted out of a loaded world, to be pasted into it or into another.</summary>
public sealed record CopiedEntity(FcbObject Node, string SourceWorld)
{
    /// <summary>A copy is an exact clone apart from identity and placement, so an entity that owns
    /// other entities cannot be one: the clone would claim the original's children.</summary>
    public static CopiedEntity Of(WorldEntity entity, string world)
    {
        FcbObject? children = entity.Node.Children.FirstOrDefault(c => c.TypeHash == WorldHashes.EntityChildren);
        if (children is { Children.Count: > 0 })
        {
            throw new InvalidOperationException(
                $"'{entity.Name}' is a prefab owning {children.Children.Count} other entities; copy those instead.");
        }

        return new CopiedEntity(entity.Node.Clone(), world);
    }

    public string ArchetypeName => FcbEntityFields.ReadString(Node, WorldHashes.TplCreatureType);
}

/// <summary>One entity's fragment as a save stages it.</summary>
public sealed record EntityFragment(string ContainerPath, string FragmentId, FcbObject Node);

/// <summary>A placed entity removed from its sector.</summary>
public sealed record DeletedEntity(string ContainerPath, ulong Id, string Name);

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

    /// <summary>Each field-edited entity's own copy of its node; kept past a save so a later edit
    /// builds on the saved one.</summary>
    private readonly Dictionary<WorldEntity, FcbObject> _working = [];

    public Fc2World World => world;

    public bool IsDirty => _touched.Count + _deleted.Count + _added.Count > 0;

    /// <summary>Deleted since the last save.</summary>
    public IReadOnlyCollection<WorldEntity> Deleted => _deleted;

    /// <summary>Added, moved or field-edited since the last save.</summary>
    public bool IsModified(WorldEntity entity) => _added.ContainsKey(entity) || _touched.Contains(entity);

    /// <summary>Adds a clone of <paramref name="copy"/> at <paramref name="position"/>, filed under
    /// <c>main</c> in the sector file that position falls in.</summary>
    public WorldEntity Paste(CopiedEntity copy, Vector3 position)
    {
        WorldSectorDocument sector = SectorAt(position) ?? throw NoSector();
        (ulong id, string name) = NewIdentity(FcbEntityFields.ReadString(copy.Node, WorldHashes.HidName));
        FcbObject node = copy.Node.Clone();
        node.Values[WorldHashes.DisEntityId] = BitConverter.GetBytes(id);
        node.Values[WorldHashes.HidName] = FcbEntityFields.StringBytes(name);
        return Add(copy, node, sector, MissionLayers.MainName, id, name, position);
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

        byte[] at = FcbEntityFields.Vector3Bytes(position);
        var events = new FcbObject { TypeHash = WorldHashes.CEventComponent };
        events.Values[WorldHashes.HidHasAliasName] = [0];
        events.Children.Add(new FcbObject { TypeHash = WorldHashes.HidLinks });
        var components = new FcbObject { TypeHash = WorldHashes.Components };
        components.Children.Add(events);
        var node = new FcbObject { TypeHash = WorldHashes.Entity };
        node.Values[WorldHashes.TplCreatureType] = FcbEntityFields.StringBytes(archetype.Name);
        node.Values[WorldHashes.HidName] = FcbEntityFields.StringBytes(name);
        node.Values[WorldHashes.DisEntityId] = BitConverter.GetBytes(id);
        node.Values[WorldHashes.HidPos] = at;
        node.Values[WorldHashes.HidAngles] = FcbEntityFields.Vector3Bytes(default);
        node.Values[WorldHashes.HidPosPrecise] = (byte[])at.Clone();
        node.Children.Add(components);
        return Add(new CopiedEntity(node, world.Name), node, sector, layerPathId, id, name, position);
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

    /// <summary>Records a change made through <see cref="EditableNode"/>.</summary>
    public void Edited(WorldEntity entity)
    {
        if (!_added.ContainsKey(entity))
        {
            _touched.Add(entity);
        }
    }

    public void Delete(WorldEntity entity)
    {
        world.Entities.Remove(entity);
        _touched.Remove(entity);
        _working.Remove(entity);
        if (!_added.Remove(entity))
        {
            _deleted.Add(entity);
        }
    }

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
    }

    /// <summary>The entity's node with its edits and placement written in; the node itself is left
    /// alone. An entity with no angles of its own gets them only once it is actually turned.</summary>
    private FcbObject Placed(WorldEntity entity)
    {
        FcbObject node = (_working.GetValueOrDefault(entity) ?? entity.Node).Clone();
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
        world.Entities.Add(entity);
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
    {
        (int x, int y) = WorldModels.SectorOf(position);
        return x >= 0 && y >= 0 && x < sectorsPerSide && y < sectorsPerSide
            ? world.SectorsById.GetValueOrDefault(y * sectorsPerSide + x)
            : null;
    }

    /// <summary>An id no loaded entity has, and <paramref name="stem"/> numbered past every name taken.</summary>
    private (ulong Id, string Name) NewIdentity(string stem)
    {
        ulong id;
        do
        {
            id = NewIdFloor | (BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) & (NewIdFloor - 1));
        }
        while (world.Entities.Any(e => e.Id == id));

        HashSet<string> taken = [.. world.Entities.Select(e => e.Name)];
        int n = 1;
        while (taken.Contains($"{stem}_{n}"))
        {
            n++;
        }
        return (id, $"{stem}_{n}");
    }
}

/// <summary>What a pasted entity needs from files besides its own sector.</summary>
public static class WorldEditDependencies
{
    public static string DepLoadPathOf(string world) => $@"worlds\{world}\generated\{world}{ContainerFormats.DepLoadSuffix}";

    /// <summary>The library single-player reads for <paramref name="world"/> - see
    /// docs/docs/engine-internals/entity-instancing.md.</summary>
    public static string LibraryPathOf(string world) => ArchetypeIndex.BaseLayer(world).Path;

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
