namespace JackAll.Tools.World;

/// <summary>One grouping row of the entity hierarchy, with its sub-groups and the entities directly
/// under it.</summary>
/// <param name="LayerPathId">The mission layer, on a layer row only.</param>
public sealed record HierarchyGroup(
    string Label, IReadOnlyList<HierarchyGroup> Groups, IReadOnlyList<WorldEntity> Entities, string? LayerPathId = null);

/// <summary>
/// How the Map tab files a world's entities: mission layer first, since the layer decides whether
/// an entity exists at all; then archetype-bound entities under their archetype's namespace path,
/// the split the Library tab uses, and standalone ones under their name family.
/// </summary>
public static class EntityHierarchy
{
    public const string FromArchetype = "From archetype";
    public const string Standalone = "Standalone";

    /// <summary>Above this many siblings a name family is split into numbered buckets - one world ships
    /// over 45,000 <c>StaticObject_*</c>, which is not a list anyone can scroll.</summary>
    private const int BucketThreshold = 400;

    private const int BucketSize = 1000;

    /// <summary>One row per mission layer, <c>main</c> first and the rest by path.</summary>
    public static IReadOnlyList<HierarchyGroup> Build(IEnumerable<WorldEntity> entities, ArchetypeIndex index)
        => [.. entities
            .GroupBy(e => e.LayerPathId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => MissionLayersOrder(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(layer => new HierarchyGroup(
                layer.Key.Length == 0 ? "(unnamed)" : layer.Key, Sections(layer, index), [], layer.Key))];

    /// <summary>An entity's name, or its id when it has none.</summary>
    public static string LabelOf(WorldEntity entity)
        => entity.Name.Length > 0 ? entity.Name : $"#{entity.Id}";

    private static int MissionLayersOrder(string path)
        => Core.Format.Fcb.MissionLayers.IsMain(path) ? 0 : 1;

    private static List<HierarchyGroup> Sections(IEnumerable<WorldEntity> entities, ArchetypeIndex index)
    {
        var sections = new List<HierarchyGroup>();
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
            foreach (IGrouping<string, WorldEntity> family in standalone.GroupBy(e => FamilyOf(e.Name), StringComparer.OrdinalIgnoreCase))
            {
                Folder folder = root.Sub(family.Key);
                if (family.Count() <= BucketThreshold)
                {
                    folder.Entities.AddRange(family);
                    continue;
                }
                foreach (IGrouping<int, WorldEntity> bucket in family.GroupBy(e => NumberOf(e.Name) / BucketSize))
                {
                    folder.Sub($"{bucket.Key * BucketSize:N0}+", bucket.Key).Entities.AddRange(bucket);
                }
            }
            sections.Add(root.Freeze());
        }
        return sections;
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

        public HierarchyGroup Freeze()
            => new(label,
                [.. _subs.Values
                    .OrderBy(f => f.Order)
                    .ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
                    .Select(f => f.Freeze())],
                [.. Entities.OrderBy(LabelOf, StringComparer.OrdinalIgnoreCase)]);

        private string Label => label;

        private int Order => order;
    }
}
