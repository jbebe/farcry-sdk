using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>
/// One row of the Map tab's hierarchy: a mission layer, a grouping, or an entity. Every flag lives
/// here rather than on the row container, which virtualization recycles.
/// </summary>
public sealed class EntityTreeNode : TreeNodeBase<EntityTreeNode>
{
    private bool _isHidden;
    private bool _isLocked;
    private bool _isModified;
    private bool _isChecked = true;

    private EntityTreeNode(string label, WorldEntity? entity, string? layerPathId)
    {
        Label = label;
        Entity = entity;
        LayerPathId = layerPathId;
    }

    public string Label { get; }

    /// <summary>Null for a grouping row.</summary>
    public WorldEntity? Entity { get; }

    /// <summary>The mission layer, on a layer row only.</summary>
    public string? LayerPathId { get; }

    public bool IsEntity => Entity is not null;

    public bool IsLayer => LayerPathId is not null;

    /// <summary>Deleted since the last save; listed only while showing modified entities.</summary>
    public bool IsDeleted { get; init; }

    /// <summary>Entities at or below this row; 1 for an entity row.</summary>
    public int Count { get; private set; }

    public string Header => IsEntity ? Label : $"{Label} ({Count:N0})";

    public bool IsHidden
    {
        get => _isHidden;
        set => Set(ref _isHidden, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => Set(ref _isLocked, value);
    }

    public bool IsModified
    {
        get => _isModified;
        set => Set(ref _isModified, value);
    }

    /// <summary>A layer row's visibility tick: unticked, none of its entities draw or list.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }

    /// <summary>One row per mission layer, with each entity's row indexed for reveal and flag updates.</summary>
    public static List<EntityTreeNode> Build(
        IReadOnlyList<HierarchyGroup> layers, ISet<WorldEntity> deleted, Dictionary<WorldEntity, EntityTreeNode> byEntity)
    {
        byEntity.Clear();
        List<EntityTreeNode> roots = [.. layers.Select(layer => Build(layer, deleted, byEntity))];
        foreach (EntityTreeNode root in roots)
        {
            CountEntities(root);
        }
        return roots;
    }

    /// <summary>Hides every entity row that does not match, and every group left empty. Layer rows
    /// always stay, so an unticked layer can be ticked again.</summary>
    public static void ApplyFilter(IEnumerable<EntityTreeNode> roots, Func<EntityTreeNode, bool> matches)
    {
        foreach (EntityTreeNode root in roots)
        {
            ApplyFilter(root, node => node.IsEntity && matches(node));
            root.IsVisible = true;
        }
    }

    public void ExpandPath()
    {
        for (EntityTreeNode? parent = Parent; parent is not null; parent = parent.Parent)
        {
            parent.IsExpanded = true;
        }
    }

    /// <summary>This row's entity, or every entity below it.</summary>
    public IEnumerable<EntityTreeNode> EntityRows()
        => IsEntity ? [this] : Children.SelectMany(c => c.EntityRows());

    private static EntityTreeNode Build(
        HierarchyGroup group, ISet<WorldEntity> deleted, Dictionary<WorldEntity, EntityTreeNode> byEntity)
    {
        var node = new EntityTreeNode(group.Label, null, group.LayerPathId);
        foreach (HierarchyGroup sub in group.Groups)
        {
            node.AddChild(Build(sub, deleted, byEntity));
        }
        foreach (WorldEntity entity in group.Entities)
        {
            var row = new EntityTreeNode(EntityHierarchy.LabelOf(entity), entity, null) { IsDeleted = deleted.Contains(entity) };
            byEntity[entity] = row;
            node.AddChild(row);
        }
        return node;
    }

    private static int CountEntities(EntityTreeNode node)
        => node.Count = node.IsEntity ? 1 : node.Children.Sum(CountEntities);
}
