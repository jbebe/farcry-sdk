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
/// The Map tab's unsaved edits to one loaded world: entities pasted, moved and deleted. Entity nodes
/// stay pristine until <see cref="Pending"/> writes each edited placement into a clone.
/// </summary>
public sealed class WorldEditSession(Fc2World world, int sectorsPerSide)
{
    /// <summary>Above every retail id (they start 0x1C8...), so a pasted id cannot collide with one.</summary>
    private const ulong NewIdFloor = 0x4000_0000_0000_0000;

    private readonly HashSet<WorldEntity> _moved = [];
    private readonly HashSet<WorldEntity> _deleted = [];
    private readonly Dictionary<WorldEntity, CopiedEntity> _pasted = [];

    public Fc2World World => world;

    public bool IsDirty => _moved.Count + _deleted.Count + _pasted.Count > 0;

    /// <summary>Adds a clone of <paramref name="copy"/> at <paramref name="position"/>, filed under
    /// <c>main</c> in the sector file that position falls in.</summary>
    public WorldEntity Paste(CopiedEntity copy, Vector3 position)
    {
        WorldSectorDocument sector = SectorAt(position)
            ?? throw new InvalidOperationException("There is no sector file at that spot to place an entity in.");

        ulong id = NewId();
        string name = UniqueName(FcbEntityFields.ReadString(copy.Node, WorldHashes.HidName));
        FcbObject node = copy.Node.Clone();
        node.Values[WorldHashes.DisEntityId] = BitConverter.GetBytes(id);
        node.Values[WorldHashes.HidName] = FcbEntityFields.StringBytes(name);

        var entity = new WorldEntity
        {
            Node = node,
            HomeSector = sector,
            LayerPathId = MissionLayers.MainName,
            Id = id,
            Name = name,
            ArchetypeName = copy.ArchetypeName,
            Position = position,
            Angles = FcbEntityFields.ReadVector3(node, WorldHashes.HidAngles) ?? default,
            IsNew = true,
        };
        world.Entities.Add(entity);
        _pasted[entity] = copy;
        return entity;
    }

    /// <summary>An unsaved paste follows its position into whichever sector file it lands in; any other
    /// entity stays filed in the sector it was loaded from.</summary>
    public void Moved(WorldEntity entity)
    {
        if (!_pasted.ContainsKey(entity))
        {
            _moved.Add(entity);
        }
        else if (SectorAt(entity.Position!.Value) is { } sector)
        {
            entity.HomeSector = sector;
        }
    }

    public void Delete(WorldEntity entity)
    {
        world.Entities.Remove(entity);
        _moved.Remove(entity);
        if (!_pasted.Remove(entity))
        {
            _deleted.Add(entity);
        }
    }

    /// <summary>Every source a pending paste was copied from.</summary>
    public IEnumerable<CopiedEntity> Pastes => _pasted.Values;

    /// <summary>The fragments to stage and the entities to delete, per sector file.</summary>
    public (IReadOnlyList<EntityFragment> Fragments, IReadOnlyList<DeletedEntity> Deleted) Pending()
    {
        List<EntityFragment> fragments = [.. _pasted.Keys.Concat(_moved).Select(entity =>
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
        foreach (WorldEntity entity in _pasted.Keys)
        {
            entity.IsNew = false;
        }
        _pasted.Clear();
        _moved.Clear();
        _deleted.Clear();
    }

    /// <summary>The entity's node with its edited placement written in; the node itself is left alone.</summary>
    private static FcbObject Placed(WorldEntity entity)
    {
        FcbObject node = entity.Node.Clone();
        byte[] position = FcbEntityFields.Vector3Bytes(entity.Position!.Value);
        node.Values[WorldHashes.HidPos] = position;
        if (node.Values.ContainsKey(WorldHashes.HidPosPrecise))
        {
            node.Values[WorldHashes.HidPosPrecise] = (byte[])position.Clone();
        }
        if (node.Values.ContainsKey(WorldHashes.HidAngles))
        {
            node.Values[WorldHashes.HidAngles] = FcbEntityFields.Vector3Bytes(entity.Angles);
        }
        return node;
    }

    private WorldSectorDocument? SectorAt(Vector3 position)
    {
        (int x, int y) = WorldModels.SectorOf(position);
        return x >= 0 && y >= 0 && x < sectorsPerSide && y < sectorsPerSide
            ? world.SectorsById.GetValueOrDefault(y * sectorsPerSide + x)
            : null;
    }

    private ulong NewId()
    {
        while (true)
        {
            ulong id = NewIdFloor | (BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)) & (NewIdFloor - 1));
            if (!world.Entities.Any(e => e.Id == id))
            {
                return id;
            }
        }
    }

    private string UniqueName(string stem)
    {
        HashSet<string> taken = [.. world.Entities.Select(e => e.Name)];
        for (int n = 1; ; n++)
        {
            string name = $"{stem}_{n}";
            if (!taken.Contains(name))
            {
                return name;
            }
        }
    }
}

/// <summary>What a pasted entity needs from files besides its own sector.</summary>
public static class WorldEditDependencies
{
    public static string DepLoadPathOf(string world) => $@"worlds\{world}\generated\{world}{ContainerFormats.DepLoadSuffix}";

    /// <summary>The library single-player reads for <paramref name="world"/> - see
    /// docs/docs/engine-internals/entity-instancing.md.</summary>
    public static string LibraryPathOf(string world) => ArchetypeIndex.BaseLayer(world, LibraryProfile.Server).Path;

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
