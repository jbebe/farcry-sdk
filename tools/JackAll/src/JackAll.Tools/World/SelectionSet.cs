namespace JackAll.Tools.World;

/// <summary>The Map tab's selected entities. <see cref="Primary"/>, the last one added, is what the
/// gizmo pivots on and the inspector shows.</summary>
public sealed class SelectionSet
{
    private readonly List<WorldEntity> _items = [];
    private readonly HashSet<WorldEntity> _lookup = [];

    public IReadOnlyList<WorldEntity> Items => _items;

    public WorldEntity? Primary => _items.Count > 0 ? _items[^1] : null;

    public int Count => _items.Count;

    public event Action? Changed;

    public void Replace(IEnumerable<WorldEntity> entities)
    {
        _items.Clear();
        _lookup.Clear();
        Append(entities);
        Changed?.Invoke();
    }

    /// <summary>Adds each entity not already selected; one already in keeps its place, so the
    /// primary only moves to something new.</summary>
    public void AddRange(IEnumerable<WorldEntity> entities)
    {
        if (Append(entities))
        {
            Changed?.Invoke();
        }
    }

    public void Toggle(WorldEntity entity)
    {
        if (_lookup.Remove(entity))
        {
            _items.Remove(entity);
        }
        else
        {
            _lookup.Add(entity);
            _items.Add(entity);
        }
        Changed?.Invoke();
    }

    public void RemoveRange(IEnumerable<WorldEntity> entities)
    {
        int before = _lookup.Count;
        _lookup.ExceptWith(entities);
        if (_lookup.Count != before)
        {
            _items.RemoveAll(e => !_lookup.Contains(e));
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }
        _items.Clear();
        _lookup.Clear();
        Changed?.Invoke();
    }

    private bool Append(IEnumerable<WorldEntity> entities)
    {
        bool added = false;
        foreach (WorldEntity entity in entities)
        {
            if (_lookup.Add(entity))
            {
                _items.Add(entity);
                added = true;
            }
        }
        return added;
    }
}
