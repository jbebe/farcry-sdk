using JackAll.Core.Format.Fcb;
using JackAll.Tools.Fcb;

namespace JackAll.Tools.World;

/// <summary>Which side of a merged node a field's value comes from.</summary>
public enum FieldOrigin
{
    InstanceOnly,
    Overridden,
    Inherited,

    /// <summary>Set by neither side, so the engine keeps its own default.</summary>
    Unset,
}

/// <summary>One field of a merged node: the value the engine reads, and the base's own.</summary>
public sealed record MergedField(uint Hash, byte[] Value, byte[]? BaseValue, FieldOrigin Origin);

/// <summary>
/// An editable node read over a base the way <c>CReadOnlyMergeNode</c> reads an instance over its
/// archetype, plus the writes an editor needs. Children pair by tag, first unpaired match; the
/// instance wins every field it has. A node with no base reads as all its own. See
/// docs/docs/engine-internals/entity-instancing.md.
/// </summary>
public sealed class MergedNode
{
    private List<MergedNode>? _children;

    private MergedNode(FcbObject? instance, FcbObject? @base, MergedNode? parent)
    {
        Instance = instance;
        Base = @base;
        Parent = parent;
    }

    /// <summary>The instance's own node, or null while every field here is inherited.</summary>
    public FcbObject? Instance { get; private set; }

    /// <summary>What the instance merges over: an archetype, or null.</summary>
    public FcbObject? Base { get; }

    public MergedNode? Parent { get; }

    public uint TypeHash => (Instance ?? Base)!.TypeHash;

    /// <summary>The node the engine would read first for display: the instance, else the base.</summary>
    public FcbObject Shown => (Instance ?? Base)!;

    public IReadOnlyList<MergedNode> Children => Kids;

    private List<MergedNode> Kids => _children ??= BuildChildren();

    /// <summary>Instance fields in instance order, then the base's remaining ones.</summary>
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
                    Base?.Values.TryGetValue(hash, out inherited);
                    fields.Add(new MergedField(
                        hash, value, inherited, inherited is null ? FieldOrigin.InstanceOnly : FieldOrigin.Overridden));
                }
            }
            if (Base is not null)
            {
                foreach ((uint hash, byte[] value) in Base.Values)
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

    /// <summary>The value the engine reads for <paramref name="hash"/>, or null when neither side sets it.</summary>
    public byte[]? ValueOf(uint hash)
        => Instance?.Values.GetValueOrDefault(hash) ?? Base?.Values.GetValueOrDefault(hash);

    /// <summary>The typed members of <paramref name="cls"/> neither side sets, at their zero value; the
    /// engine keeps its own default for each until one is written.</summary>
    public IEnumerable<MergedField> UnsetFields(FcbClass cls) => cls.AllMembers()
        .Where(m => m.Member.Type != FcbMemberType.BinHex
                    && Instance?.Values.ContainsKey(m.Hash) != true && Base?.Values.ContainsKey(m.Hash) != true)
        .Select(m => new MergedField(
            m.Hash, FcbValueCodec.Encode(m.Member.Type, FcbFieldFormat.DefaultValue(m.Member.Type)), null, FieldOrigin.Unset));

    /// <summary>The merge of <paramref name="instance"/> over <paramref name="base"/>; a node with no
    /// base passes null and every field reads as its own.</summary>
    public static MergedNode Of(FcbObject instance, FcbObject? @base)
        => new(instance, @base, null);

    /// <summary>A standalone copy of what the engine reads here, for a merge to stack on as its base.</summary>
    public FcbObject Flatten()
    {
        var flat = new FcbObject { TypeHash = TypeHash };
        foreach (MergedField field in Fields)
        {
            flat.Values[field.Hash] = field.Value;
        }
        flat.Children.AddRange(Children.Select(c => c.Flatten()));
        return flat;
    }

    /// <summary>For each item of <paramref name="left"/>, the index of its partner in
    /// <paramref name="right"/> - the first unpaired one with the same tag - or -1.</summary>
    public static int[] PairByTag<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, Func<T, uint> tagOf)
    {
        var byTag = new Dictionary<uint, Queue<int>>();
        for (int r = 0; r < right.Count; r++)
        {
            uint tag = tagOf(right[r]);
            if (!byTag.TryGetValue(tag, out Queue<int>? queue))
            {
                byTag[tag] = queue = new Queue<int>();
            }
            queue.Enqueue(r);
        }

        var partners = new int[left.Count];
        for (int l = 0; l < left.Count; l++)
        {
            partners[l] = byTag.GetValueOrDefault(tagOf(left[l]))?.TryDequeue(out int r) == true ? r : -1;
        }
        return partners;
    }

    /// <summary>Writes a field into the instance, creating the instance side of this node first when
    /// it is inherited whole.</summary>
    public void SetValue(uint hash, byte[] value) => Materialize().Values[hash] = value;

    /// <summary>Appends <paramref name="child"/> to the instance, creating the instance side of this
    /// node first when it is inherited whole. A tag the base already has here would pair with the
    /// base's child instead, so that is refused.</summary>
    public MergedNode AddChild(FcbObject child)
    {
        if (Kids.Any(c => c.TypeHash == child.TypeHash && c.Base is not null))
        {
            throw new InvalidOperationException("The base already has a child with this tag.");
        }
        Materialize().Children.Add(child);
        var node = new MergedNode(child, null, this);
        Kids.Add(node);
        return node;
    }

    /// <summary>Removes a child the instance added. A base's child cannot be removed from an
    /// instance: the engine merges, it never subtracts.</summary>
    public void RemoveChild(MergedNode child)
    {
        if (child.Base is not null || child.Instance is null || !Kids.Remove(child))
        {
            throw new InvalidOperationException("Only a child the instance added can be removed.");
        }
        Instance!.Children.Remove(child.Instance);
    }

    /// <summary>Removes the instance's own value so the base's shows through again, and drops
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

    private List<MergedNode> BuildChildren()
    {
        List<FcbObject> own = Instance?.Children ?? [];
        List<FcbObject> inherited = Base?.Children ?? [];
        int[] partners = PairByTag(own, inherited, c => c.TypeHash);
        var pairs = new FcbObject?[inherited.Count];
        var children = new List<MergedNode>(Math.Max(own.Count, inherited.Count));
        for (int i = 0; i < own.Count; i++)
        {
            if (partners[i] >= 0)
            {
                pairs[partners[i]] = own[i];
            }
        }

        for (int i = 0; i < inherited.Count; i++)
        {
            children.Add(new MergedNode(pairs[i], inherited[i], this));
        }
        for (int i = 0; i < own.Count; i++)
        {
            if (partners[i] < 0)
            {
                children.Add(new MergedNode(own[i], null, this));
            }
        }
        return children;
    }

    /// <summary>
    /// The instance node for this position, created on demand. Pairing is by tag in order, so a base
    /// child's same-tag siblings ahead of it get an empty instance node too, or the new one would pair
    /// with the wrong slot.
    /// </summary>
    private FcbObject Materialize()
    {
        if (Instance is not null)
        {
            return Instance;
        }

        FcbObject parent = Parent!.Materialize();
        foreach (MergedNode sibling in Parent.Kids)
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
        List<MergedNode> siblings = Parent.Kids;
        for (int i = siblings.Count - 1; i >= 0; i--)
        {
            MergedNode sibling = siblings[i];
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
            if (sibling.Base is null)
            {
                siblings.RemoveAt(i);
            }
        }
        return Instance is null;
    }
}
