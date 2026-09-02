using System.Globalization;
using System.Text;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Move;
using JackAll.Core.Format;
using JackAll.Core.Naming;

namespace JackAll.Core.Mods;

/// <summary>
/// A MOVE animation graph (`movemgr.bin`, `dlc1.bin`) as an <see cref="IContainerSplitter"/>: one
/// fragment per state.
/// </summary>
/// <remarks>
/// Without this a mod that retargets a single animation clip ships the whole 1.8 MB graph, and two
/// mods that each touch an animation cannot coexist - whole-file overrides are last-wins and silent,
/// so the loser's work disappears with no diagnostic.
///
/// A fragment is staged as <c>&lt;label&gt;.&lt;m_stateNameHash decimal&gt;.xml</c>, the scheme
/// `depload` uses, so <see cref="FcbFragments.IdComparer"/> collapses the label with no special case.
/// The number binds and the label is there to read by - which matters more here than anywhere else,
/// because the loadable graph carries <em>no state names at all</em> (they live only in the
/// `movemgrnamed.bin` twin the engine refuses to load). JackAll can therefore only ever list a bare
/// number, while a mod author holding the twin knows <c>Pawn_Generic_Aim</c>; binding on the number
/// lets both land on one entry.
///
/// See docs/docs/file-formats/move.md and docs/design/mod-layout-final.md.
/// </remarks>
public sealed class MoveContainerSplitter : IContainerSplitter
{
    /// <summary>
    /// The graphs that split. The `*named.bin` twins are deliberately absent: they set
    /// <c>dwFileFormat &amp; 0x20000</c>, which <c>CMoveMgr::CreateFromStream</c> rejects outright, so
    /// they are authoring artifacts no engine will load.
    /// </summary>
    private static readonly string[] Graphs = ["movemgr.bin", "dlc1.bin"];

    public static MoveContainerSplitter Instance { get; } = new();

    public static bool IsMoveGraph(string fileName)
        => Graphs.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The fragment id a state is staged under: <c>&lt;label&gt;.&lt;hash decimal&gt;.xml</c>, or a
    /// bare <c>&lt;hash&gt;.xml</c> when there is no name to read it by - which is the normal case,
    /// since the loadable graph holds no names.
    /// </summary>
    public static string IdOf(uint stateHash, string? name = null)
    {
        string label = Sanitize(name);
        return label.Length == 0 ? $"{stateHash}.xml" : $"{label}.{stateHash}.xml";
    }

    /// <summary>The state hash a fragment id names, read through the same canonicalization
    /// <see cref="FcbFragments.IdComparer"/> keys on, so two ids that comparer calls equal resolve to
    /// one state here too.</summary>
    public static uint? StateOf(string fragmentId) => HashOf(fragmentId);

    private static uint? HashOf(string fragmentId)
    {
        if (!fragmentId.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string stem = FcbFragments.Canonicalize(fragmentId)[..^".xml".Length];
        if (stem.Length == 0)
        {
            return null;
        }

        // Canonicalization has already reduced a labelled id to its number. Anything left that is not
        // numeric names the state outright, which still builds - it just cannot compare equal to the
        // labelled form, so tooling never writes one.
        return uint.TryParse(stem, NumberStyles.None, CultureInfo.InvariantCulture, out uint hash)
            ? hash
            : NameHash.Compute(stem);
    }

    /// <summary>A bare filename, with anything a path or a filesystem would object to reduced to an
    /// underscore.</summary>
    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> leaf = name.AsSpan(name.AsSpan().LastIndexOfAny('\\', '/') + 1).Trim();
        StringBuilder text = new(leaf.Length);
        foreach (char c in leaf)
        {
            text.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }

        return text.ToString();
    }

    public IContainerTree Open(byte[] container) => new Tree(MoveCodec.Load(container));

    public string Canonicalize(string fragmentXml)
        => MoveFragmentXml.Render(MoveFragmentXml.Parse(fragmentXml));

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        MoveFile file = MoveCodec.Load(baseBytes);
        MoveStateIndex index = MoveStateIndex.Build(file);

        Dictionary<uint, MoveFragment> staged = [];
        foreach ((string id, string xml) in fragmentXmlById)
        {
            MoveFragment fragment = MoveFragmentXml.Parse(xml);
            if (HashOf(id) != fragment.StateHash)
            {
                throw new InvalidDataException(
                    $"A MOVE fragment staged as '{id}' describes state {fragment.StateHash} instead. "
                    + $"Name it '{IdOf(fragment.StateHash)}' - any label ahead of the number is yours "
                    + "to choose - or fix the state it names.");
            }

            if (index.ByHash(fragment.StateHash) is { } existing && index.IsNested(existing))
            {
                throw new InvalidDataException(
                    $"State {fragment.StateHash} is not its own fragment: it is nested inside another "
                    + "state's subtree and travels with it. Override the top-level state that "
                    + $"contains it ('{IdOf(TopLevelOwnerHash(index, existing))}') instead.");
            }

            staged[fragment.StateHash] = fragment;
        }

        // Every reference that survives the splice but points into a state being replaced has to be
        // re-seated afterwards, because the objects it names are about to be discarded. Capture them
        // as addresses first: after the swap there is no way back from a dead pointer to what it meant.
        HashSet<MoveObject> doomed = [];
        foreach (uint hash in staged.Keys)
        {
            if (index.ByHash(hash) is { } state && !index.IsNested(state))
            {
                doomed.Add(state);
            }
        }

        List<(MoveObject Owner, int Index, MoveAddress Address)> inbound = [];
        foreach (MoveObject obj in file.Objects)
        {
            if (index.StateOf(obj) is { } owning && doomed.Contains(owning))
            {
                continue;   // this object is being replaced; its own pointers go with it
            }

            for (int i = 0; i < obj.Ops.Count; i++)
            {
                MoveOp op = obj.Ops[i];
                if (op.Kind == MoveOpKind.PointerRef
                    && index.StateOf(op.Target!) is { } target && doomed.Contains(target))
                {
                    inbound.Add((obj, i, index.AddressOf(op.Target!)!.Value));
                }
            }
        }

        Rebuild(file, index, staged);
        MoveStateIndex rebuilt = MoveStateIndex.Build(file);

        foreach ((MoveObject owner, int at, MoveAddress address) in inbound)
        {
            owner.Ops[at] = owner.Ops[at].WithTarget(Seat(rebuilt, address, "a state it references"));
        }

        foreach (MoveFragment fragment in staged.Values)
        {
            foreach (((MoveObject owner, int at), MoveAddress address) in fragment.External)
            {
                owner.Ops[at] = owner.Ops[at].WithTarget(
                    Seat(rebuilt, address, $"state {fragment.StateHash}"));
            }
        }

        return MoveCodec.Save(file);
    }

    /// <summary>
    /// Replaces the state machine's slot list: overridden states swap in place, new ones append.
    /// </summary>
    /// <remarks>
    /// Order comes from the base file and additions go on the end, so no fragment ever carries one -
    /// the same reasoning that keeps <c>childIndex</c> out of a `depload` fragment. Appending is
    /// always safe because registration is pre-order and a back-reference can only point backwards.
    ///
    /// A slot is written as a new object when it is the state's own, and as a back-reference when the
    /// state is nested inside another state's subtree. <c>nbState</c> is the number of <em>slots</em>,
    /// not of distinct states: 13 of <c>movemgr.bin</c>'s 1,700 slots are such back-references.
    /// </remarks>
    private static void Rebuild(
        MoveFile file, MoveStateIndex index, Dictionary<uint, MoveFragment> staged)
    {
        List<uint> order = [];
        List<MoveObject> roots = [];
        foreach (MoveObject slot in index.Slots)
        {
            uint hash = MoveStateIndex.NameHashOf(slot)!.Value;
            order.Add(hash);
            if (index.IsNested(slot))
            {
                continue;
            }

            roots.Add(staged.TryGetValue(hash, out MoveFragment? replacement)
                ? replacement.Root
                : slot);
        }

        HashSet<uint> known = [.. order];
        foreach (uint hash in staged.Keys.Where(h => !known.Contains(h)).Order())
        {
            order.Add(hash);
            roots.Add(staged[hash].Root);
        }

        // Which object each hash now names, counting states nested inside a top-level one.
        Dictionary<uint, MoveObject> byHash = [];
        HashSet<MoveObject> topLevel = [.. roots];
        foreach (MoveObject root in roots)
        {
            Collect(root, byHash);
        }

        MoveObject machine = index.StateMachine;
        List<MoveOp> ops = [];
        HashSet<MoveObject> emitted = [];
        foreach (MoveOp op in machine.Ops)
        {
            if (op.Name == "CMoveBaseState")
            {
                continue;   // rewritten below, in one run
            }

            ops.Add(op.Name == "nbState" ? op.WithNumber((uint)order.Count) : op);
        }

        foreach (uint hash in order)
        {
            if (!byHash.TryGetValue(hash, out MoveObject? state))
            {
                throw new InvalidDataException(
                    $"State {hash} has a slot in the graph but no longer exists. A fragment that "
                    + "removes a nested state has to remove its slot too, which this format cannot "
                    + "express - keep the nested state.");
            }

            bool isNew = topLevel.Contains(state) && emitted.Add(state);
            ops.Add(MoveOp.Pointer(
                isNew ? MoveOpKind.PointerNew : MoveOpKind.PointerRef, "CMoveBaseState", state));
        }

        machine.Ops.Clear();
        machine.Ops.AddRange(ops);
        file.Reindex();
    }

    /// <summary>Every state-classed object in one subtree, keyed by its name hash.</summary>
    private static void Collect(MoveObject node, Dictionary<uint, MoveObject> into)
    {
        if (MoveStateIndex.NameHashOf(node) is { } hash)
        {
            into[hash] = node;
        }

        foreach (MoveOp op in node.Ops)
        {
            if (op.Kind == MoveOpKind.PointerNew)
            {
                Collect(op.Target!, into);
            }
        }
    }

    private static MoveObject Seat(MoveStateIndex index, MoveAddress address, string who)
        => index.Resolve(address)
           ?? throw new InvalidDataException(
               $"{who} points at {address}, which the rebuilt graph no longer has. A mod restructured "
               + "that state - added, removed or reordered the objects under it - and broke a "
               + "reference another state makes into it. Rebuild both together, or leave the "
               + "referenced state's shape alone.");

    private static uint TopLevelOwnerHash(MoveStateIndex index, MoveObject nested)
        => MoveStateIndex.NameHashOf(index.StateOf(nested) ?? nested) ?? 0;

    private sealed class Tree(MoveFile file) : IContainerTree
    {
        private readonly MoveStateIndex _index = MoveStateIndex.Build(file);

        public string? Extract(string fragmentId)
        {
            if (HashOf(fragmentId) is not { } hash
                || _index.ByHash(hash) is not { } state
                || _index.IsNested(state))
            {
                return null;
            }

            return MoveFragmentXml.Render(MoveFragmentXml.Lift(_index, state));
        }

        /// <summary>
        /// One row per top-level state. The size is the subtree's object count rather than its
        /// rendered length: a graph holds 22,000 objects across 1,687 states, and rendering every one
        /// just to measure it would build 25 MB of text nobody reads.
        /// </summary>
        public IReadOnlyList<FcbFragmentInfo> List()
        {
            List<FcbFragmentInfo> rows = [];
            foreach (MoveObject state in _index.TopLevelStates)
            {
                if (MoveStateIndex.NameHashOf(state) is not { } hash)
                {
                    continue;
                }

                rows.Add(new FcbFragmentInfo(IdOf(hash), Weigh(state)));
            }

            return rows;
        }

        private static long Weigh(MoveObject node)
        {
            long total = 1;
            foreach (MoveOp op in node.Ops)
            {
                if (op.Kind == MoveOpKind.PointerNew)
                {
                    total += Weigh(op.Target!);
                }
            }

            return total;
        }
    }
}
