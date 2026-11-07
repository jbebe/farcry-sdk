namespace JackAll.Tools.World;

/// <summary>
/// Items filed under the sector they stand in, so a ring of sectors around a point visits only what
/// stands in those sectors rather than the whole world.
/// </summary>
public sealed class SectorIndex<T>
{
    private readonly Dictionary<(int X, int Y), List<T>> _sectors = [];

    public static SectorIndex<T> Of(IEnumerable<T> items, Func<T, System.Numerics.Vector3> positionOf)
    {
        var index = new SectorIndex<T>();
        foreach (T item in items)
        {
            index.Add(WorldModels.SectorOf(positionOf(item)), item);
        }
        return index;
    }

    public void Add((int X, int Y) sector, T item)
    {
        if (!_sectors.TryGetValue(sector, out List<T>? items))
        {
            _sectors[sector] = items = [];
        }
        items.Add(item);
    }

    public bool Remove((int X, int Y) sector, T item)
        => _sectors.TryGetValue(sector, out List<T>? items) && items.Remove(item);

    public void Clear() => _sectors.Clear();

    /// <summary>What stands in each sector within <paramref name="ring"/> of <paramref name="centre"/>,
    /// with that sector's Chebyshev distance from it - so the ring is a square of sectors.</summary>
    public IEnumerable<(int Distance, List<T> Items)> Ring((int X, int Y) centre, int ring)
    {
        for (int y = centre.Y - ring; y <= centre.Y + ring; y++)
        {
            for (int x = centre.X - ring; x <= centre.X + ring; x++)
            {
                if (_sectors.TryGetValue((x, y), out List<T>? items))
                {
                    yield return (Math.Max(Math.Abs(x - centre.X), Math.Abs(y - centre.Y)), items);
                }
            }
        }
    }
}
