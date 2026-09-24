using System.Numerics;
using System.Windows.Input;
using JackAll.Tools.World;

namespace JackAll.App.MapEditor;

/// <summary>
/// The Map tab's hierarchy: every placed entity under its mission layer, with the filters, the layer
/// ticks and the per-entity hide and lock toggles that decide what the viewport draws and picks.
/// Hide and lock are editor state for this session only; nothing about them reaches the game.
/// </summary>
public sealed class HierarchyViewModel : Observable
{
    private readonly SelectionSet _selection;
    private readonly Func<Vector3> _camera;
    private readonly Dictionary<WorldEntity, EntityTreeNode> _rows = [];
    private readonly HashSet<WorldEntity> _hidden = [];
    private readonly HashSet<WorldEntity> _locked = [];
    private readonly HashSet<string> _uncheckedLayers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EntityTreeNode> _highlighted = [];
    private List<WorldEntity> _entities = [];
    private IReadOnlyList<EntityTreeNode> _roots = [];
    private Func<WorldEntity, bool> _isModified = _ => false;
    private string _search = "";
    private bool _modifiedOnly;
    private bool _nearCamera;
    private double _nearRange = 50;
    private Vector3 _nearPoint;
    private string _countText = "";
    private EntityTreeNode? _anchor;

    /// <param name="camera">Where the camera is, for the "near camera" filter.</param>
    public HierarchyViewModel(SelectionSet selection, Func<Vector3> camera)
    {
        _selection = selection;
        _camera = camera;
        selection.Changed += SyncHighlight;
    }

    public IReadOnlyList<EntityTreeNode> Roots
    {
        get => _roots;
        private set { _roots = value; OnPropertyChanged(); }
    }

    /// <summary>Entities in a ticked layer and not hidden: what the viewport draws.</summary>
    public List<WorldEntity> VisibleEntities { get; private set; } = [];

    /// <summary>Raised when <see cref="VisibleEntities"/> changes.</summary>
    public event Action? VisibleSetChanged;

    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) ApplyFilter(); }
    }

    public bool ModifiedOnly
    {
        get => _modifiedOnly;
        set { if (Set(ref _modifiedOnly, value)) ApplyFilter(); }
    }

    /// <summary>Lists only entities near where the camera stood when this was switched on.</summary>
    public bool NearCamera
    {
        get => _nearCamera;
        set
        {
            _nearPoint = _camera();
            if (Set(ref _nearCamera, value)) ApplyFilter();
        }
    }

    public double NearRange
    {
        get => _nearRange;
        set { if (Set(ref _nearRange, value) && _nearCamera) ApplyFilter(); }
    }

    public string CountText
    {
        get => _countText;
        private set => Set(ref _countText, value);
    }

    /// <summary>Whether a click in the viewport can land on the entity.</summary>
    public bool IsPickable(WorldEntity entity) => !_locked.Contains(entity);

    /// <summary>Whether a gizmo may move the entity: drawn and not locked.</summary>
    public bool CanEdit(WorldEntity entity)
        => !_locked.Contains(entity) && !_hidden.Contains(entity) && !_uncheckedLayers.Contains(entity.LayerPathId);

    /// <summary>Rebuilds the tree for a new entity set, keeping the layer ticks and the hide and lock
    /// toggles of the entities still in it.</summary>
    public void Rebuild(
        IReadOnlyList<WorldEntity> entities, IReadOnlyCollection<WorldEntity> deleted, ArchetypeIndex index,
        Func<WorldEntity, bool> isModified, Func<WorldEntity, Core.Format.Fcb.FcbObject>? nodeOf = null)
    {
        _entities = [.. entities];
        _isModified = isModified;
        _hidden.IntersectWith(entities);
        _locked.IntersectWith(entities);
        var deletedSet = new HashSet<WorldEntity>(deleted);

        List<EntityTreeNode> roots = EntityTreeNode.Build(
            EntityHierarchy.Build(entities.Concat(deleted), index, nodeOf), deletedSet, _rows);
        foreach (EntityTreeNode layer in roots)
        {
            layer.IsChecked = !_uncheckedLayers.Contains(layer.LayerPathId!);
        }
        foreach ((WorldEntity entity, EntityTreeNode row) in _rows)
        {
            row.IsHidden = _hidden.Contains(entity);
            row.IsLocked = _locked.Contains(entity);
            row.IsModified = isModified(entity) || row.IsDeleted;
        }
        _highlighted.Clear();
        _anchor = null;
        Roots = roots;
        SyncHighlight();
        ApplyFilter();
        RecomputeVisible();
    }

    /// <summary>Re-reads the modified flag of the given entities, or of every row.</summary>
    public void RefreshModified(IEnumerable<WorldEntity>? entities = null)
    {
        foreach (WorldEntity entity in entities ?? _rows.Keys)
        {
            if (_rows.TryGetValue(entity, out EntityTreeNode? row))
            {
                row.IsModified = _isModified(entity) || row.IsDeleted;
            }
        }
        if (_modifiedOnly)
        {
            ApplyFilter();
        }
    }

    /// <summary>A click on a row. Ctrl toggles it; Shift adds the rows between the last click and
    /// this one; a group row stands for every entity shown below it.</summary>
    public void Click(EntityTreeNode node, ModifierKeys modifiers)
    {
        if (node.IsDeleted)
        {
            return;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift) && _anchor is not null)
        {
            _selection.AddRange(Between(_anchor, node));
            return;
        }

        _anchor = node;
        if (!modifiers.HasFlag(ModifierKeys.Control))
        {
            _selection.Replace(Selectable(node));
        }
        else if (node.IsEntity)
        {
            _selection.Toggle(node.Entity!);
        }
        else
        {
            _selection.AddRange(Selectable(node));
        }
    }

    /// <summary>The next shown entity row after the last clicked row, or before it for a negative
    /// <paramref name="direction"/>; group and deleted rows are stepped over.</summary>
    public EntityTreeNode? Step(int direction)
    {
        List<EntityTreeNode> shown = [.. Roots.SelectMany(Shown)];
        int at = _anchor is null ? -1 : shown.IndexOf(_anchor);
        if (at < 0)
        {
            at = direction > 0 ? -1 : shown.Count;
        }
        for (int i = at + direction; i >= 0 && i < shown.Count; i += direction)
        {
            if (shown[i] is { IsEntity: true, IsDeleted: false })
            {
                return shown[i];
            }
        }
        return null;
    }

    /// <summary>The row for <paramref name="entity"/>, with the path to it expanded.</summary>
    public EntityTreeNode? Reveal(WorldEntity entity)
    {
        if (!_rows.TryGetValue(entity, out EntityTreeNode? row))
        {
            return null;
        }
        _anchor = row;
        row.ExpandPath();
        return row;
    }

    /// <summary>Hides or shows a row's entities; a hidden entity leaves the selection too.</summary>
    public void ToggleHidden(EntityTreeNode node)
    {
        List<WorldEntity> changed = Toggle(node, _hidden, (row, on) => row.IsHidden = on, node.IsHidden = !node.IsHidden);
        if (node.IsHidden)
        {
            _selection.RemoveRange(changed);
        }
        RecomputeVisible();
    }

    public void ToggleLocked(EntityTreeNode node)
        => Toggle(node, _locked, (row, on) => row.IsLocked = on, node.IsLocked = !node.IsLocked);

    /// <summary>Called once a layer row's tick has changed.</summary>
    public void LayerToggled(EntityTreeNode layer)
    {
        SetMember(_uncheckedLayers, layer.LayerPathId!, !layer.IsChecked);
        ApplyFilter();
        RecomputeVisible();
    }

    public void SetAllLayers(Func<string, bool> visible)
    {
        foreach (EntityTreeNode layer in Roots)
        {
            layer.IsChecked = visible(layer.LayerPathId!);
            SetMember(_uncheckedLayers, layer.LayerPathId!, !layer.IsChecked);
        }
        ApplyFilter();
        RecomputeVisible();
    }

    public void ApplyFilter()
    {
        int shown = 0;
        EntityTreeNode.ApplyFilter(Roots, row =>
        {
            WorldEntity entity = row.Entity!;
            bool matches = (_modifiedOnly ? row.IsModified : !row.IsDeleted && !_uncheckedLayers.Contains(entity.LayerPathId))
                && (!_nearCamera || entity.Position is not { } at || Vector3.Distance(at, _nearPoint) <= _nearRange)
                && (_search.Length == 0
                    || entity.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
                    || entity.ArchetypeName.Contains(_search, StringComparison.OrdinalIgnoreCase));
            shown += matches ? 1 : 0;
            return matches;
        });
        CountText = shown == _entities.Count ? $"{_entities.Count:N0} entities" : $"{shown:N0} of {_entities.Count:N0} entities";
    }

    private static List<WorldEntity> Toggle(
        EntityTreeNode node, HashSet<WorldEntity> set, Action<EntityTreeNode, bool> flag, bool on)
    {
        List<WorldEntity> changed = [];
        foreach (EntityTreeNode row in node.EntityRows())
        {
            flag(row, on);
            SetMember(set, row.Entity!, on);
            changed.Add(row.Entity!);
        }
        return changed;
    }

    private static void SetMember<T>(HashSet<T> set, T item, bool member)
    {
        if (member)
        {
            set.Add(item);
        }
        else
        {
            set.Remove(item);
        }
    }

    private void RecomputeVisible()
    {
        VisibleEntities = [.. _entities.Where(e => !_hidden.Contains(e) && !_uncheckedLayers.Contains(e.LayerPathId))];
        VisibleSetChanged?.Invoke();
    }

    private static List<WorldEntity> Selectable(EntityTreeNode node)
        => [.. node.EntityRows().Where(r => r.IsVisible && !r.IsDeleted && !r.IsHidden).Select(r => r.Entity!)];

    /// <summary>The entity rows from one row to another in the order the tree shows them.</summary>
    private IEnumerable<WorldEntity> Between(EntityTreeNode from, EntityTreeNode to)
    {
        List<EntityTreeNode> shown = [.. Roots.SelectMany(Shown)];
        int a = shown.IndexOf(from), b = shown.IndexOf(to);
        if (a < 0 || b < 0)
        {
            return Selectable(to);
        }
        (a, b) = (Math.Min(a, b), Math.Max(a, b));
        return shown.Skip(a).Take(b - a + 1).SelectMany(Selectable).Distinct();
    }

    /// <summary>Rows a user can see: visible, and under expanded parents.</summary>
    private static IEnumerable<EntityTreeNode> Shown(EntityTreeNode node)
    {
        if (!node.IsVisible)
        {
            yield break;
        }
        yield return node;
        if (!node.IsExpanded)
        {
            yield break;
        }
        foreach (EntityTreeNode child in node.Children)
        {
            foreach (EntityTreeNode shown in Shown(child))
            {
                yield return shown;
            }
        }
    }

    /// <summary>Marks the selected entities' rows; the tree's own single selection is not used.</summary>
    private void SyncHighlight()
    {
        foreach (EntityTreeNode row in _highlighted)
        {
            row.IsSelected = false;
        }
        _highlighted.Clear();
        foreach (WorldEntity entity in _selection.Items)
        {
            if (_rows.TryGetValue(entity, out EntityTreeNode? row))
            {
                row.IsSelected = true;
                _highlighted.Add(row);
            }
        }
    }
}
