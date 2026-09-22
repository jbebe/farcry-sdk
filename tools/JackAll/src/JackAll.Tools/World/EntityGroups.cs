using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>One member a prefab lists, by the name and id of a sibling entity.</summary>
public sealed record PrefabChild(string Name, ulong Id);

/// <summary>
/// Reads and writes the <c>Children</c> list a prefab entity names its members in. Members are ordinary
/// entities elsewhere in the file, not nested nodes - see
/// docs/docs/engine-internals/entity-instancing.md.
/// </summary>
public static class EntityGroups
{
    public const string PrefabClass = "CPrefabEntity";

    private static readonly uint Child = FcbClassDefinitions.Crc32Ascii("Child");
    private static readonly uint Name = FcbClassDefinitions.Crc32Ascii("Name");
    private const uint ChildId = 0x11D3633A;

    /// <summary>The entity's own class name, which only a class-bound entity such as a prefab carries.</summary>
    public const uint EntityClassName = 0xD2B3429E;

    public static IReadOnlyList<PrefabChild> ChildrenOf(FcbObject entity)
        => ListOf(entity) is { } list
            ? [.. list.Children.Where(c => c.TypeHash == Child)
                .Select(c => new PrefabChild(FcbEntityFields.ReadString(c, Name), FcbEntityFields.ReadU64(c, ChildId)))]
            : [];

    public static bool IsPrefab(FcbObject entity) => ListOf(entity) is not null;

    /// <summary>Replaces the entity's member list, creating it when it has none.</summary>
    public static void SetChildren(FcbObject entity, IEnumerable<PrefabChild> children)
    {
        FcbObject list = ListOf(entity) ?? AddList(entity);
        list.Children.Clear();
        foreach (PrefabChild child in children)
        {
            var node = new FcbObject { TypeHash = Child };
            node.Values[Name] = FcbEntityFields.StringBytes(child.Name);
            node.Values[ChildId] = BitConverter.GetBytes(child.Id);
            list.Children.Add(node);
        }
    }

    /// <summary>Which entity lists each one as a member, over <paramref name="entities"/>.</summary>
    public static IReadOnlyDictionary<ulong, WorldEntity> ParentsById(
        IEnumerable<WorldEntity> entities, Func<WorldEntity, FcbObject> nodeOf)
    {
        var parents = new Dictionary<ulong, WorldEntity>();
        foreach (WorldEntity entity in entities)
        {
            foreach (PrefabChild child in ChildrenOf(nodeOf(entity)))
            {
                parents.TryAdd(child.Id, entity);
            }
        }
        return parents;
    }

    private static FcbObject? ListOf(FcbObject entity)
        => entity.Children.FirstOrDefault(c => c.TypeHash == WorldHashes.EntityChildren);

    private static FcbObject AddList(FcbObject entity)
    {
        var list = new FcbObject { TypeHash = WorldHashes.EntityChildren };
        entity.Children.Add(list);
        return list;
    }
}
