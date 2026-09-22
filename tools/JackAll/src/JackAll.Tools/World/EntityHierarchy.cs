namespace JackAll.Tools.World;

/// <summary>One grouping row of the entity hierarchy, with its sub-groups and the entities directly
/// under it.</summary>
/// <param name="LayerPathId">The mission layer, on a layer row only.</param>
public sealed record HierarchyGroup(
    string Label, IReadOnlyList<HierarchyGroup> Groups, IReadOnlyList<WorldEntity> Entities, string? LayerPathId = null);

/// <summary>
/// How the Map tab files a world's entities: mission layer first, since the layer decides whether
/// an entity exists at all; then prefabs with their members, archetype-bound entities under their
/// archetype's namespace path, the split the Library tab uses, and standalone ones under their name
/// family.
/// </summary>
public static class EntityHierarchy
{
    public const string FromArchetype = "From archetype";
    public const string Standalone = "Standalone";
    public const string Prefabs = "Prefabs";

    /// <summary>Above this many siblings a name family is split into numbered buckets - one world ships
    /// over 45,000 <c>StaticObject_*</c>, which is not a list anyone can scroll.</summary>
    private const int BucketThreshold = 400;

    private const int BucketSize = 1000;

    /// <summary>One row per mission layer, <c>main</c> first and the rest by path.</summary>
    /// <param name="nodeOf">The node to read prefab members from; the loaded one by default.</param>
    public static IReadOnlyList<HierarchyGroup> Build(
        IEnumerable<WorldEntity> entities, ArchetypeIndex index, Func<WorldEntity, Core.Format.Fcb.FcbObject>? nodeOf = null)
        => [.. entities
            .GroupBy(e => e.LayerPathId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => MissionLayersOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(layer => new HierarchyGroup(
                layer.Key.Length == 0 ? "(unnamed)" : layer.Key, Sections(layer, index, nodeOf ?? (e => e.Node)), [], layer.Key))];

    /// <summary>
    /// Each prefab in the layer, as a group holding the prefab entity and the members it lists, so
    /// selecting the group selects the lot. Returns the entities filed there.
    /// </summary>
    private static HashSet<WorldEntity> FilePrefabs(
        IReadOnlyList<WorldEntity> entities, Func<WorldEntity, Core.Format.Fcb.FcbObject> nodeOf, List<HierarchyGroup> into)
    {
        Dictionary<ulong, WorldEntity> byId = [];
        foreach (WorldEntity entity in entities)
        {
            byId.TryAdd(entity.Id, entity);
        }

        var filed = new HashSet<WorldEntity>();
        var root = new Folder(Prefabs);
        List<WorldEntity> prefabs = [.. entities.Where(e => EntityGroups.IsPrefab(nodeOf(e)))];
        FileByFamily(root, prefabs, (folder, prefab) =>
        {
            List<WorldEntity> members = [.. EntityGroups.ChildrenOf(nodeOf(prefab))
                .Select(c => byId.GetValueOrDefault(c.Id))
                .OfType<WorldEntity>()
                .Where(m => !filed.Contains(m))];
            Folder group = folder.Sub(LabelOf(prefab));
            group.KeepsOrder = true;
            group.Entities.AddRange([.. members, prefab]);
            filed.Add(prefab);
            filed.UnionWith(members);
        });
        if (prefabs.Count > 0)
        {
            into.Add(root.Freeze());
        }
        return filed;
    }

    /// <summary>An entity's name, or its id when it has none.</summary>
    public static string LabelOf(WorldEntity entity)
        => entity.Name.Length > 0 ? entity.Name : $"#{entity.Id}";

    private static int MissionLayersOrder(string path)
        => Core.Format.Fcb.MissionLayers.IsMain(path) ? 0 : 1;

    private static List<HierarchyGroup> Sections(
        IEnumerable<WorldEntity> layer, ArchetypeIndex index, Func<WorldEntity, Core.Format.Fcb.FcbObject> nodeOf)
    {
        var sections = new List<HierarchyGroup>();
        List<WorldEntity> all = [.. layer];
        HashSet<WorldEntity> inPrefabs = FilePrefabs(all, nodeOf, sections);
        List<WorldEntity> entities = [.. all.Where(e => !inPrefabs.Contains(e))];
        List<WorldEntity> bound = [.. entities.Where(e => e.ArchetypeName.Length > 0)];
        if (bound.Count > 0)
        {
            var root = new Folder(FromArchetype);
            foreach (IGrouping<string, WorldEntity> group in bound.GroupBy(e => e.ArchetypeName, StringComparer.OrdinalIgnoreCase))
            {
                (IReadOnlyList<string> namespaces, string label) = index.SplitForDisplay(group.Key);
                Folder folder = root;
                foreach (string step in namespaces.Append(label))
                {
                    folder = folder.Sub(step);
                }
                folder.Entities.AddRange(group);
            }
            sections.Add(root.Freeze());
        }

        List<WorldEntity> standalone = [.. entities.Where(e => e.ArchetypeName.Length == 0)];
        if (standalone.Count > 0)
        {
            var root = new Folder(Standalone);
            FileByFamily(root, standalone, (folder, entity) => folder.Entities.Add(entity));
            sections.Add(root.Freeze());
        }
        return sections;
    }

    /// <summary>Files each entity under its name family, split into numbered buckets past
    /// <see cref="BucketThreshold"/>.</summary>
    private static void FileByFamily(Folder root, IEnumerable<WorldEntity> entities, Action<Folder, WorldEntity> file)
    {
        foreach (IGrouping<string, WorldEntity> family in entities.GroupBy(e => FamilyOf(e.Name), StringComparer.OrdinalIgnoreCase))
        {
            Folder folder = root.Sub(family.Key);
            if (family.Count() <= BucketThreshold)
            {
                foreach (WorldEntity entity in family)
                {
                    file(folder, entity);
                }
                continue;
            }
            foreach (IGrouping<int, WorldEntity> bucket in family.GroupBy(e => NumberOf(e.Name) / BucketSize))
            {
                Folder numbered = folder.Sub($"{bucket.Key * BucketSize:N0}+", bucket.Key);
                foreach (WorldEntity entity in bucket)
                {
                    file(numbered, entity);
                }
            }
        }
    }

    /// <summary>The name with its trailing index removed, so <c>StaticObject_2001</c> files under
    /// <c>StaticObject</c>.</summary>
    private static string FamilyOf(string name)
    {
        string family = name[..EndOfStem(name)].TrimEnd('_');
        return family.Length > 0 ? family : name.Length > 0 ? name : "(unnamed)";
    }

    private static int NumberOf(string name)
        => int.TryParse(name.AsSpan(EndOfStem(name)), out int number) ? number : 0;

    private static int EndOfStem(string name)
    {
        int end = name.Length;
        while (end > 0 && char.IsAsciiDigit(name[end - 1]))
        {
            end--;
        }
        return end;
    }

    /// <summary>A group while it is being filled; sorted into a <see cref="HierarchyGroup"/> once full.
    /// <paramref name="order"/> ranks numbered buckets ahead of their labels' text order.</summary>
    private sealed class Folder(string label, int order = 0)
    {
        private readonly Dictionary<string, Folder> _subs = new(StringComparer.OrdinalIgnoreCase);

        public List<WorldEntity> Entities { get; } = [];

        public Folder Sub(string name, int order = 0)
            => _subs.TryGetValue(name, out Folder? found) ? found : _subs[name] = new Folder(name, order);

        public string Label { get; } = label;

        public int Order { get; } = order;

        /// <summary>Lists its entities as added rather than by name - a prefab group ends on the prefab,
        /// which a group click then makes the primary selection.</summary>
        public bool KeepsOrder { get; set; }

        public HierarchyGroup Freeze()
            => new(Label,
                [.. _subs.Values
                    .OrderBy(f => f.Order)
                    .ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
                    .Select(f => f.Freeze())],
                KeepsOrder ? [.. Entities] : [.. Entities.OrderBy(LabelOf, StringComparer.OrdinalIgnoreCase)]);
    }
}
