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
}
