namespace JackAll.Core.Format.Fcb;

/// <summary>
/// One node in a Dunia .fcb entity/object tree: a type hash, a flat table of hash-keyed raw values,
/// and child objects. Nothing here carries a human name - a hash only means something once it's
/// looked up against a class/member dictionary (e.g. binary_classes.xml), which is a separate layer
/// on top of this raw tree, not part of the binary format itself.
/// </summary>
public sealed class FcbObject
{
    public uint TypeHash { get; set; }

    /// <summary>
    /// Insertion order is part of what round-trips back to identical bytes - <see cref="FcbDocument"/>
    /// writes values in the order they appear here, matching how a freshly parsed object naturally
    /// holds them (file order) and how a freshly authored one would (source-document order).
    /// </summary>
    public Dictionary<uint, byte[]> Values { get; } = [];

    public List<FcbObject> Children { get; } = [];

    /// <summary>A deep copy - no value array or child is shared with this node.</summary>
    public FcbObject Clone()
    {
        var copy = new FcbObject { TypeHash = TypeHash };
        foreach ((uint hash, byte[] value) in Values)
        {
            copy.Values[hash] = (byte[])value.Clone();
        }
        foreach (FcbObject child in Children)
        {
            copy.Children.Add(child.Clone());
        }
        return copy;
    }

    /// <summary>Makes this node a deep copy of <paramref name="source"/> while staying the same
    /// object, so whatever holds a reference to it sees the new content.</summary>
    public void Overwrite(FcbObject source)
    {
        FcbObject copy = source.Clone();
        TypeHash = copy.TypeHash;
        Values.Clear();
        foreach ((uint hash, byte[] value) in copy.Values)
        {
            Values[hash] = value;
        }
        Children.Clear();
        Children.AddRange(copy.Children);
    }
}
