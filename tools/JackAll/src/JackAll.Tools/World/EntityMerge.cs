using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>Which side of a merged entity a field's value comes from.</summary>
public enum FieldOrigin
{
    InstanceOnly,
    Overridden,
    Inherited,
}

/// <summary>One field of a merged node: the value the engine reads, and the archetype's own.</summary>
public sealed record MergedField(uint Hash, byte[] Value, byte[]? ArchetypeValue, FieldOrigin Origin);

/// <summary>
/// A placed entity as the engine reads it: the instance merged over its archetype the way
/// <c>CReadOnlyMergeNode</c> does, plus the writes an editor needs. Children pair by tag, first
/// unpaired match; the instance wins every field it has. See
/// docs/docs/engine-internals/entity-instancing.md.
/// </summary>
public sealed class MergedNode
{
    private readonly List<MergedNode> _children = [];

    private MergedNode(FcbObject? instance, FcbObject? archetype, MergedNode? parent)
    {
        Instance = instance;
        Archetype = archetype;
        Parent = parent;
    }

    /// <summary>The instance's own node, or null while every field here is inherited.</summary>
    public FcbObject? Instance { get; private set; }

    public FcbObject? Archetype { get; }

    public MergedNode? Parent { get; }

    public uint TypeHash => (Instance ?? Archetype)!.TypeHash;

    public IReadOnlyList<MergedNode> Children => _children;

    /// <summary>Instance fields in instance order, then the archetype's remaining ones.</summary>
    public IReadOnlyList<MergedField> Fields
    {
        get
        {
            var fields = new List<MergedField>();
            if (Instance is not null)
            {
                foreach ((uint hash, byte[] value) in Instance.Values)
                {
                    byte[]? inherited = null;
                    Archetype?.Values.TryGetValue(hash, out inherited);
                    fields.Add(new MergedField(
                        hash, value, inherited, inherited is null ? FieldOrigin.InstanceOnly : FieldOrigin.Overridden));
                }
            }
            if (Archetype is not null)
            {
                foreach ((uint hash, byte[] value) in Archetype.Values)
                {
                    if (Instance is null || !Instance.Values.ContainsKey(hash))
                    {
                        fields.Add(new MergedField(hash, value, value, FieldOrigin.Inherited));
                    }
                }
            }
            return fields;
        }
    }

    /// <summary>The merge of <paramref name="instance"/> over <paramref name="archetype"/>; a
    /// standalone entity passes null and every field reads as its own.</summary>
    public static MergedNode Of(FcbObject instance, FcbObject? archetype)
        => Build(instance, archetype, null);

    /// <summary>Writes a field into the instance, creating the instance side of this node first when
    /// it is inherited whole.</summary>
    public void SetValue(uint hash, byte[] value) => Materialize().Values[hash] = value;

    /// <summary>Removes the instance's own value so the archetype's shows through again, and drops
    /// instance nodes the removal leaves empty.</summary>
    public void Revert(uint hash)
    {
        if (Instance is null || !Instance.Values.Remove(hash))
        {
            return;
        }
        for (MergedNode? node = this; node is { Parent: not null }; node = node.Parent)
        {
            if (!node.TryPrune())
            {
                break;
            }
        }
    }

    private static MergedNode Build(FcbObject? instance, FcbObject? archetype, MergedNode? parent)
    {
        var node = new MergedNode(instance, archetype, parent);
        var archetypeChildren = archetype?.Children ?? [];
        var pairs = new FcbObject?[archetypeChildren.Count];
        var appended = new List<FcbObject>();
        foreach (FcbObject child in instance?.Children ?? [])
        {
            int slot = -1;
            for (int i = 0; i < archetypeChildren.Count; i++)
            {
                if (pairs[i] is null && archetypeChildren[i].TypeHash == child.TypeHash)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                appended.Add(child);
            }
            else
            {
                pairs[slot] = child;
            }
        }

        for (int i = 0; i < archetypeChildren.Count; i++)
        {
            node._children.Add(Build(pairs[i], archetypeChildren[i], node));
        }
        foreach (FcbObject child in appended)
        {
            node._children.Add(Build(child, null, node));
        }
        return node;
    }

    /// <summary>
    /// The instance node for this position, created on demand. Pairing is by tag in order, so an
    /// archetype child's same-tag siblings ahead of it get an empty instance node too, or the new one
    /// would pair with the wrong slot.
    /// </summary>
    private FcbObject Materialize()
    {
        if (Instance is not null)
        {
            return Instance;
        }

        FcbObject parent = Parent!.Materialize();
        foreach (MergedNode sibling in Parent._children)
        {
            if (sibling.TypeHash == TypeHash && sibling.Instance is null)
            {
                sibling.Instance = new FcbObject { TypeHash = TypeHash };
                parent.Children.Add(sibling.Instance);
            }
            if (ReferenceEquals(sibling, this))
            {
                break;
            }
        }
        return Instance!;
    }

    /// <summary>
    /// Drops the empty instance nodes of this tag from the end of the parent's run of them. Only the
    /// tail can go: removing one ahead of a non-empty sibling would shift that sibling's pairing.
    /// Returns whether this node's own instance side went.
    /// </summary>
    private bool TryPrune()
    {
        if (Parent!.Instance is not { } parent)
        {
            return false;
        }
        for (int i = Parent._children.Count - 1; i >= 0; i--)
        {
            MergedNode sibling = Parent._children[i];
            if (sibling.TypeHash != TypeHash || sibling.Instance is null)
            {
                continue;
            }
            if (sibling.Instance is not { Values.Count: 0, Children.Count: 0 } empty)
            {
                break;
            }

            parent.Children.Remove(empty);
            sibling.Instance = null;
            if (sibling.Archetype is null)
            {
                Parent._children.RemoveAt(i);
            }
        }
        return Instance is null;
    }
}
