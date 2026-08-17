using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>
/// One row in the Map tab's entity tree. The top split is the engine's own: an entity either names an
/// archetype in <c>tplCreatureType</c> and merges over it, or it stands alone and is read as-is. Most
/// of a world is the second kind, so grouping everything under archetypes would leave three quarters
/// of it homeless.
/// </summary>
public sealed class EntityTreeNode : TreeNodeBase<EntityTreeNode>
{
    /// <summary>Above this many siblings a name family is split into numbered buckets - one world ships
    /// over 45,000 <c>StaticObject_*</c>, which is not a list anyone can scroll.</summary>
    private const int BucketThreshold = 400;

    private const int BucketSize = 1000;

    private EntityTreeNode(string label, WorldEntity? entity)
    {
        Label = label;
        Entity = entity;
    }

    public string Label { get; }

    /// <summary>Null for a grouping row.</summary>
    public WorldEntity? Entity { get; }

    public bool IsEntity => Entity is not null;

    /// <summary>Entities at or below this row; 1 for an entity row.</summary>
    public int Count { get; private set; }

    /// <summary>What the tree shows: a group carries its size, the way a folder does. An entity's id
    /// and sector are left to the inspect box, which already lists both.</summary>
    public string Header => IsEntity ? Label : $"{Label} ({Count:N0})";

    /// <summary>
    /// Archetype-bound entities under their archetype's own namespace path (the same split the Library
    /// tab uses, so the two trees read alike), everything else under its name family.
    /// </summary>
    public static EntityTreeNode Build(IReadOnlyList<WorldEntity> entities, ArchetypeIndex index)
    {
        var root = new EntityTreeNode("Entities", null);

        List<WorldEntity> bound = [.. entities.Where(e => e.ArchetypeName.Length > 0)];
        if (bound.Count > 0)
        {
            EntityTreeNode section = root.GroupFor("From archetype");
            foreach (IGrouping<string, WorldEntity> group in bound
                .GroupBy(e => e.ArchetypeName, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                (IReadOnlyList<string> namespaces, string label) = index.SplitForDisplay(group.Key);
                EntityTreeNode parent = section;
                foreach (string step in namespaces.Append(label))
                {
                    parent = parent.GroupFor(step);
                }
                AddAll(parent, group);
            }
        }

        List<WorldEntity> standalone = [.. entities.Where(e => e.ArchetypeName.Length == 0)];
        if (standalone.Count > 0)
        {
            EntityTreeNode section = root.GroupFor("Standalone");
            foreach (IGrouping<string, WorldEntity> family in standalone
                .GroupBy(e => FamilyOf(e.Name), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            {
                EntityTreeNode node = section.GroupFor(family.Key);
                if (family.Count() <= BucketThreshold)
                {
                    AddAll(node, family);
                    continue;
                }

                foreach (IGrouping<int, WorldEntity> bucket in family
                    .GroupBy(e => NumberOf(e.Name) / BucketSize)
                    .OrderBy(g => g.Key))
                {
                    AddAll(node.GroupFor($"{bucket.Key * BucketSize:N0}+"), bucket);
                }
            }
        }

        CountEntities(root);
        return root;
    }

    /// <summary>Shows only entities matching the search text and sitting in a ticked mission layer;
    /// returns how many are left.</summary>
    public static int ApplyFilter(EntityTreeNode root, string filter, ISet<string> visibleLayers)
    {
        int matched = 0;
        ApplyFilter(root, node =>
        {
            if (node.Entity is not { } entity
                || !visibleLayers.Contains(entity.LayerPathId)
                || (filter.Length > 0
                    && !entity.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !entity.ArchetypeName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            matched++;
            return true;
        });
        return matched;
    }

    /// <summary>Selects the row for one entity, expanding the path to it - used when the viewport is clicked.</summary>
    public static EntityTreeNode? Reveal(EntityTreeNode root, WorldEntity entity)
        => Reveal(root, node => ReferenceEquals(node.Entity, entity));

    private static void AddAll(EntityTreeNode parent, IEnumerable<WorldEntity> entities)
    {
        foreach (WorldEntity entity in entities.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            parent.AddChild(new EntityTreeNode(
                entity.Name.Length > 0 ? entity.Name : $"#{entity.Id}", entity));
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

    private static int CountEntities(EntityTreeNode node)
        => node.Count = node.IsEntity ? 1 : node.Children.Sum(CountEntities);

    private EntityTreeNode GroupFor(string label)
    {
        foreach (EntityTreeNode child in Children)
        {
            if (!child.IsEntity && child.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        var group = new EntityTreeNode(label, null);
        AddChild(group);
        return group;
    }
}
