using System.Globalization;

namespace JackAll.Core.Format.Move;

/// <summary>
/// Where one object sits, named by the state that owns it rather than by its position in the file.
/// </summary>
/// <remarks>
/// A MOVE graph addresses objects by their index in registration order, which shifts whenever
/// anything earlier in the file changes. A fragment cannot carry such an index for the same reason a
/// `depload` fragment carries no <c>childIndex</c>, so a reference that leaves a fragment is written
/// as this instead: the owning state's <c>m_stateNameHash</c>, plus the route down to the object.
///
/// <see cref="Path"/> is the chain of <em>child ordinals</em> - at each step, which of that object's
/// created children to descend into - dotted, and empty for the state itself. Ordinals rather than op
/// indices because they survive any edit that does not insert, remove or reorder child objects; an op
/// index would also move when a scalar field was added.
/// </remarks>
public readonly record struct MoveAddress(uint StateHash, string Path)
{
    public override string ToString() =>
        Path.Length == 0 ? StateHash.ToString("X8") : $"{StateHash:X8}/{Path}";
}

/// <summary>
/// A MOVE graph seen as states: which objects each one owns, and how to name one from outside.
/// </summary>
/// <remarks>
/// Built once per container because every question below is answered against the same three maps and
/// a graph is 22,000 objects. See docs/docs/file-formats/move.md.
/// </remarks>
public sealed class MoveStateIndex
{
    private readonly Dictionary<MoveObject, MoveObject> _owners;
    private readonly Dictionary<MoveObject, MoveObject> _stateOf;
    private readonly Dictionary<uint, MoveObject> _byHash;

    private MoveStateIndex(
        MoveFile file,
        MoveObject stateMachine,
        List<MoveObject> slots,
        Dictionary<MoveObject, MoveObject> owners,
        Dictionary<MoveObject, MoveObject> stateOf,
        Dictionary<uint, MoveObject> byHash)
    {
        File = file;
        StateMachine = stateMachine;
        Slots = slots;
        _owners = owners;
        _stateOf = stateOf;
        _byHash = byHash;
    }

    public MoveFile File { get; }

    public MoveObject StateMachine { get; }

    /// <summary>
    /// The state machine's list, in file order. Its length is <c>nbState</c>, which is <em>not</em>
    /// the number of distinct states: 13 of <c>movemgr.bin</c>'s 1,700 slots are back-references to
    /// states nested inside another state's subtree.
    /// </summary>
    public IReadOnlyList<MoveObject> Slots { get; }

    /// <summary>The states a fragment can address: those the state machine itself owns.</summary>
    public IEnumerable<MoveObject> TopLevelStates => Slots.Where(s => _owners.GetValueOrDefault(s) == StateMachine);

    /// <summary>The state a hash names, whether it is top-level or nested inside another.</summary>
    public MoveObject? ByHash(uint hash) => _byHash.GetValueOrDefault(hash);

    /// <summary>True when this state is listed but owned by another state, so it is not its own
    /// fragment - it travels inside the fragment of whichever top-level state contains it.</summary>
    public bool IsNested(MoveObject state) => _owners.GetValueOrDefault(state) != StateMachine;

    /// <summary>The top-level state whose subtree holds this object, or null for the manager's own
    /// scaffolding (the manager, the value container, the machine, its transition refs).</summary>
    public MoveObject? StateOf(MoveObject obj) => _stateOf.GetValueOrDefault(obj);

    public static uint? NameHashOf(MoveObject state) => state.Field("m_stateNameHash");

    public static MoveStateIndex Build(MoveFile file)
    {
        MoveObject machine = file.StateMachine
            ?? throw new MoveFormatException("this MOVE file holds no CMoveStateMachine");

        Dictionary<MoveObject, MoveObject> owners = [];
        foreach (MoveObject obj in file.Objects)
        {
            foreach (MoveOp op in obj.Ops)
            {
                if (op.Kind == MoveOpKind.PointerNew)
                {
                    owners[op.Target!] = obj;
                }
            }
        }

        List<MoveObject> slots = [];
        foreach (MoveOp op in machine.Ops)
        {
            if (op.Target is { } state && op.Name == "CMoveBaseState")
            {
                slots.Add(state);
            }
        }

        // Attribute every object to the top-level state containing it. Walking each subtree once is
        // what keeps this linear; climbing the owner chain per object would be O(n*depth), and
        // cross-references reach 29 levels down.
        Dictionary<MoveObject, MoveObject> stateOf = [];
        foreach (MoveObject state in slots.Where(s => owners.GetValueOrDefault(s) == machine))
        {
            Stack<MoveObject> pending = new();
            pending.Push(state);
            while (pending.Count > 0)
            {
                MoveObject node = pending.Pop();
                stateOf[node] = state;
                foreach (MoveOp op in node.Ops)
                {
                    if (op.Kind == MoveOpKind.PointerNew)
                    {
                        pending.Push(op.Target!);
                    }
                }
            }
        }

        Dictionary<uint, MoveObject> byHash = [];
        foreach (MoveObject state in slots)
        {
            if (NameHashOf(state) is { } hash)
            {
                byHash[hash] = state;
            }
        }

        return new MoveStateIndex(file, machine, slots, owners, stateOf, byHash);
    }

    /// <summary>The children one object creates, in the order it creates them.</summary>
    public static List<MoveObject> ChildrenOf(MoveObject obj)
    {
        List<MoveObject> children = [];
        foreach (MoveOp op in obj.Ops)
        {
            if (op.Kind == MoveOpKind.PointerNew)
            {
                children.Add(op.Target!);
            }
        }

        return children;
    }

    /// <summary>
    /// How to name <paramref name="obj"/> from outside its state, or null when it belongs to the
    /// manager's scaffolding rather than to any state.
    /// </summary>
    public MoveAddress? AddressOf(MoveObject obj)
    {
        if (StateOf(obj) is not { } state || NameHashOf(state) is not { } hash)
        {
            return null;
        }

        List<int> path = [];
        for (MoveObject node = obj; node != state;)
        {
            MoveObject parent = _owners[node];
            path.Add(ChildrenOf(parent).IndexOf(node));
            node = parent;
        }

        path.Reverse();
        return new MoveAddress(hash, string.Join('.', path));
    }

    /// <summary>The object an address names, or null when the route no longer resolves - which is
    /// what a mod restructuring a state another mod points into looks like.</summary>
    public MoveObject? Resolve(MoveAddress address)
        => ByHash(address.StateHash) is { } state ? Walk(state, address.Path) : null;

    /// <summary>Follows a dotted child-ordinal path down from one state.</summary>
    public static MoveObject? Walk(MoveObject state, string path)
    {
        if (path.Length == 0)
        {
            return state;
        }

        MoveObject node = state;
        foreach (string step in path.Split('.'))
        {
            if (!int.TryParse(step, NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal))
            {
                return null;
            }

            List<MoveObject> children = ChildrenOf(node);
            if (ordinal < 0 || ordinal >= children.Count)
            {
                return null;
            }

            node = children[ordinal];
        }

        return node;
    }
}
