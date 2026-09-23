using JackAll.Core.Format.Fcb;
using JackAll.Tools.World;

namespace JackAll.App.FileHandlers.Fcb.FcbEditor;

/// <summary>
/// One row of a document's outline: the root, each entity, and every node on the way down to one.
/// An entity's insides belong to its inspector and everything else to the key/value view of the
/// nearest row above it, so the outline stops at entities.
/// </summary>
public sealed class OutlineNode : TreeNodeBase<OutlineNode>
{
    private readonly bool _compared;
    private bool _ownChanged;
    private int _changedChildren;

    private OutlineNode(FcbObject obj, FcbObject? original, FcbClass cls, string label, bool isEntity, bool compared)
    {
        Object = obj;
        Original = original;
        Class = cls;
        Label = label;
        IsEntity = isEntity;
        _compared = compared;
    }

    public FcbObject Object { get; }

    /// <summary>The same node in the document's baseline, paired by tag in order; null when it has none.</summary>
    public FcbObject? Original { get; }

    public FcbClass Class { get; }

    public string Label { get; }

    public bool IsEntity { get; }

    /// <summary>Whether this row's own part of the tree, or any row below it, differs from the baseline.</summary>
    public bool ContainsChange => _ownChanged || _changedChildren > 0;

    /// <summary>The view shown while this row is selected, kept so what a user opened stays open.</summary>
    internal object? Pane { get; set; }

    /// <param name="original">The document's baseline, or null when there is nothing to compare against.</param>
    public static OutlineNode Build(FcbObject root, FcbObject? original, FcbEditContext context, IEntityBases entities)
        => BuildNode(root, null, original, context.Definitions.Resolve(root), context, entities, original is not null)!;

    /// <summary>The row for <paramref name="obj"/>, or null when it is neither an entity nor above one.</summary>
    private static OutlineNode? BuildNode(
        FcbObject obj, FcbObject? parent, FcbObject? original, FcbClass cls, FcbEditContext context,
        IEntityBases entities, bool compared)
    {
        bool isEntity = entities.IsEntity(obj, parent);
        var rows = new List<OutlineNode>();
        if (!isEntity)
        {
            int[]? partners = original is null ? null : MergedNode.PairByTag(obj.Children, original.Children, c => c.TypeHash);
            for (int i = 0; i < obj.Children.Count; i++)
            {
                FcbObject child = obj.Children[i];
                FcbObject? partner = partners is null || partners[i] < 0 ? null : original!.Children[partners[i]];
                if (BuildNode(child, obj, partner, cls.Resolve(child), context, entities, compared) is { } row)
                {
                    rows.Add(row);
                }
            }
            if (parent is not null && rows.Count == 0)
            {
                return null;
            }
        }

        var node = new OutlineNode(obj, original, cls, FcbNodeViews.Label(obj, cls, context), isEntity, compared);
        foreach (OutlineNode row in rows)
        {
            node.AddChild(row);
            node._changedChildren += row.ContainsChange ? 1 : 0;
        }
        node._ownChanged = node.OwnPartDiffers();
        return node;
    }

    /// <summary>The children of <see cref="Object"/>, or of <see cref="Original"/>, that have rows of their own.</summary>
    internal HashSet<FcbObject?> RowObjects(bool original = false)
        => new(Children.Select(c => original ? c.Original : c.Object), ReferenceEqualityComparer.Instance);

    /// <summary>Re-compares this row's own part after an edit in it, and updates the rows above.</summary>
    public void Recompare()
    {
        bool before = ContainsChange;
        _ownChanged = OwnPartDiffers();
        NotifyIfFlipped(before);
    }

    private void NotifyIfFlipped(bool before)
    {
        if (before == ContainsChange)
        {
            return;
        }
        OnPropertyChanged(nameof(ContainsChange));
        if (Parent is { } parent)
        {
            bool parentBefore = parent.ContainsChange;
            parent._changedChildren += ContainsChange ? 1 : -1;
            parent.NotifyIfFlipped(parentBefore);
        }
    }

    /// <summary>This row's fields and every node below it that is not a row of its own.</summary>
    private bool OwnPartDiffers()
    {
        if (!_compared)
        {
            return false;
        }
        if (Original is null)
        {
            return true;
        }
        if (!SameValues(Object, Original))
        {
            return true;
        }
        if (Children.Count == 0)
        {
            return !SameTrees(Object.Children, Original.Children);
        }

        HashSet<FcbObject?> rows = RowObjects();
        HashSet<FcbObject?> originalRows = RowObjects(original: true);
        return !SameTrees(
            [.. Object.Children.Where(c => !rows.Contains(c))], [.. Original.Children.Where(c => !originalRows.Contains(c))]);
    }

    private static bool SameValues(FcbObject a, FcbObject b)
        => a.Values.Count == b.Values.Count
           && a.Values.All(v => b.Values.TryGetValue(v.Key, out byte[]? other) && v.Value.AsSpan().SequenceEqual(other));

    private static bool SameTrees(List<FcbObject> a, List<FcbObject> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        int[] partners = MergedNode.PairByTag(a, b, c => c.TypeHash);
        for (int i = 0; i < a.Count; i++)
        {
            if (partners[i] < 0 || !SameValues(a[i], b[partners[i]]) || !SameTrees(a[i].Children, b[partners[i]].Children))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ApplyFilter(OutlineNode node, string filter)
        => ApplyFilter(node, n => filter.Length == 0 || n.Label.Contains(filter, StringComparison.OrdinalIgnoreCase));

    /// <summary>Selects the first row under <paramref name="root"/> whose <paramref name="field"/> holds
    /// <paramref name="value"/>.</summary>
    public static OutlineNode? Reveal(OutlineNode root, uint field, string value)
        => Reveal(root, n => n.Object.Values.ContainsKey(field)
            && FcbEntityFields.ReadString(n.Object, field).Equals(value, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc cref="Reveal(OutlineNode, uint, string)"/>
    public static OutlineNode? Reveal(OutlineNode root, uint field, byte[] value)
        => Reveal(root, n => n.Object.Values.TryGetValue(field, out byte[]? own) && own.SequenceEqual(value));
}
