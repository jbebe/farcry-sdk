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
    /// The fragment id a unit is staged under: <c>&lt;label&gt;.&lt;number decimal&gt;.xml</c>.
    /// </summary>
    /// <remarks>
    /// For a state the number is its <c>m_stateNameHash</c>; for a weapon branch it is the hash of
    /// <see cref="MoveUnit.Key"/>, which is built only from engine-assigned values - the state's own
    /// hash, the MOVE channel index, and the <c>EquippedWeapon</c> enum value. That keeps the
    /// composite key inside the one thing <see cref="FcbFragments.IdComparer"/> can collapse, a
    /// numeric tail, and it is the same "type hash that is itself a name hash" shape `depload` uses.
    /// </remarks>
    public static string IdOf(MoveUnit unit, string? name = null)
    {
        string label = Sanitize(name ?? unit.Label);
        return label.Length == 0 ? $"{unit.Id}.xml" : $"{label}.{unit.Id}.xml";
    }

    /// <summary>The number a fragment id names, read through the same canonicalization
    /// <see cref="FcbFragments.IdComparer"/> keys on, so two ids that comparer calls equal resolve to
    /// one unit here too.</summary>
    public static uint? UnitOf(string fragmentId) => HashOf(fragmentId);

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
        Dictionary<uint, MoveUnit> known = Catalogue(index);

        // A staged unit belongs to a state; a state is rebuilt only once, from whichever of its units
        // were staged plus the rest taken from the base graph.
        Dictionary<uint, Dictionary<uint, MoveFragment>> byState = [];
        Dictionary<MoveSection, MoveFragment> sections = [];
        foreach ((string id, string xml) in fragmentXmlById)
        {
            if (MoveSections.Parse(id) is { } named)
            {
                MoveFragment slice = MoveFragmentXml.Parse(xml);
                if (slice.Section != named)
                {
                    throw new InvalidDataException(
                        $"A MOVE fragment staged as '{id}' describes the "
                        + $"{(slice.Section is { } s ? MoveSections.NameOf(s) : "no")} section instead.");
                }

                if (named == MoveSection.Channels
                    && MoveSections.DeclaredChannelCount(slice.Roots[0].Ops)
                        is { } count and not MoveSections.RequiredChannelCount)
                {
                    throw new InvalidDataException(
                        $"'{id}' declares {count} value channels. MSAnim::LoadMoves compares this "
                        + $"against a hardcoded {MoveSections.RequiredChannelCount} and drops the "
                        + "file otherwise, so the graph would load as no animation at all, silently.");
                }

                sections[named] = slice;
                continue;
            }

            MoveFragment fragment = MoveFragmentXml.Parse(xml);
            uint unitId = fragment.Unit.Id;
            if (HashOf(id) != unitId)
            {
                throw new InvalidDataException(
                    $"A MOVE fragment staged as '{id}' describes {fragment.Unit} instead, which is "
                    + $"{unitId}. Name it '{IdOf(fragment.Unit)}' - any label ahead of the number is "
                    + "yours to choose - or fix what it names.");
            }

            if (index.ByHash(fragment.StateHash) is { } existing && index.IsNested(existing))
            {
                throw new InvalidDataException(
                    $"State {fragment.StateHash} is not its own fragment: it is nested inside another "
                    + "state's subtree and travels with it. Override the top-level state that "
                    + $"contains it instead.");
            }

            byState.TryAdd(fragment.StateHash, []);
            byState[fragment.StateHash][unitId] = fragment;
        }

        // Every reference that survives but points into a state being rebuilt has to be re-seated
        // afterwards: the objects it names are about to be replaced. Capture them as addresses first,
        // because after the swap there is no way back from a dead pointer to what it meant.
        HashSet<MoveObject> doomed =
        [
            .. byState.Keys
                .Select(index.ByHash)
                .OfType<MoveObject>()
                .Where(s => !index.IsNested(s)),
        ];

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

        List<(MoveObject Owner, int Index, MoveAddress Address)> pending = [];
        Dictionary<uint, MoveObject> states = [];
        foreach ((uint stateHash, Dictionary<uint, MoveFragment> units) in byState)
        {
            (MoveObject state, List<(MoveObject, int, MoveAddress)> external) =
                AssembleState(index, known, stateHash, units);
            states[stateHash] = state;
            pending.AddRange(external);
        }

        if (sections.Count > 0)
        {
            MoveObject manager = file.Objects.FirstOrDefault(o => o.ClassName == "CMoveMgr")
                ?? throw new InvalidDataException(
                    "This graph has no CMoveMgr, so it has no manager sections to override - an "
                    + "expansion like dlc1.bin is a bare state machine.");
            MoveFragmentXml.SpliceSections(manager, sections);
            foreach (MoveFragment slice in sections.Values)
            {
                pending.AddRange(slice.External.Select(e => (e.Key.Owner, e.Key.Index, e.Value)));
            }
        }

        Rebuild(file, index, states);
        MoveStateIndex rebuilt = MoveStateIndex.Build(file);

        foreach ((MoveObject owner, int at, MoveAddress address) in inbound.Concat(pending))
        {
            owner.Ops[at] = owner.Ops[at].WithTarget(Seat(rebuilt, address));
        }

        return MoveCodec.Save(file);
    }

    /// <summary>Every unit the base graph holds, by the number a fragment id resolves to.</summary>
    private static Dictionary<uint, MoveUnit> Catalogue(MoveStateIndex index)
    {
        Dictionary<uint, MoveUnit> known = [];
        foreach (MoveObject state in index.TopLevelStates)
        {
            if (MoveStateIndex.NameHashOf(state) is not { } hash)
            {
                continue;
            }

            foreach (MoveUnit unit in MoveUnits.UnitsOf(state, hash))
            {
                known[unit.Id] = unit;
            }
        }

        return known;
    }

    /// <summary>
    /// Rebuilds one state from the units a mod staged, taking every unit it did not stage from the
    /// base graph. A mod that edits one weapon's branch never has to ship the state around it.
    /// </summary>
    private static (MoveObject State, List<(MoveObject, int, MoveAddress)> External) AssembleState(
        MoveStateIndex index,
        IReadOnlyDictionary<uint, MoveUnit> known,
        uint stateHash,
        IReadOnlyDictionary<uint, MoveFragment> staged)
    {
        MoveObject? original = index.ByHash(stateHash);
        MoveFragment remainder;
        if (staged.TryGetValue(stateHash, out MoveFragment? own))
        {
            remainder = own;
        }
        else if (original is not null)
        {
            remainder = MoveFragmentXml.LiftState(index, original);
        }
        else
        {
            throw new InvalidDataException(
                $"A branch fragment names state {stateHash}, which this graph does not have. Stage "
                + "the state itself alongside it.");
        }

        Dictionary<uint, MoveFragment> branches = [];
        foreach ((uint id, MoveFragment fragment) in staged)
        {
            if (id != stateHash)
            {
                branches[id] = fragment;
            }
        }

        // Units the mod left alone still have to be supplied, from the graph as it stands.
        if (original is not null)
        {
            foreach (MoveUnit unit in MoveUnits.UnitsOf(original, stateHash))
            {
                if (!unit.IsRemainder && !branches.ContainsKey(unit.Id))
                {
                    branches[unit.Id] = MoveFragmentXml.LiftBranches(index, original, unit);
                }
            }
        }

        return MoveFragmentXml.Assemble(remainder, branches);
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
        MoveFile file, MoveStateIndex index, Dictionary<uint, MoveObject> staged)
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

            roots.Add(staged.TryGetValue(hash, out MoveObject? replacement) ? replacement : slot);
        }

        HashSet<uint> known = [.. order];
        foreach (uint hash in staged.Keys.Where(h => !known.Contains(h)).Order())
        {
            order.Add(hash);
            roots.Add(staged[hash]);
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

    private static MoveObject Seat(MoveStateIndex index, MoveAddress address)
        => index.Resolve(address)
           ?? throw new InvalidDataException(
               $"A reference points at {address}, which the rebuilt graph no longer has. A mod "
               + "restructured that state - added, removed or reordered the objects under it - and "
               + "broke a reference something else makes into it. Rebuild both together, or leave "
               + "the referenced state's shape alone.");

    private sealed class Tree(MoveFile file) : IContainerTree
    {
        private readonly MoveStateIndex _index = MoveStateIndex.Build(file);

        /// <summary>
        /// Which manager sections this graph has - none for an expansion, which is a bare
        /// <c>CMoveStateMachine</c> with no manager and no value container at all.
        /// </summary>
        private readonly IReadOnlyCollection<MoveSection> _sections =
            MoveSections.Ranges(file) is { } ranges ? [.. ranges.Keys] : [];

        public string? Extract(string fragmentId)
        {
            // Reserved names are matched first, so a state can never shadow a section.
            if (MoveSections.Parse(fragmentId) is { } section)
            {
                return _sections.Contains(section)
                    ? MoveFragmentXml.Render(MoveFragmentXml.LiftSection(_index, section))
                    : null;
            }

            if (HashOf(fragmentId) is not { } id || Find(id) is not var (state, unit))
            {
                return null;
            }

            return MoveFragmentXml.Render(unit.IsRemainder
                ? MoveFragmentXml.LiftState(_index, state)
                : MoveFragmentXml.LiftBranches(_index, state, unit));
        }

        /// <summary>The state and unit a fragment number names, searching states then their branches.</summary>
        private (MoveObject State, MoveUnit Unit)? Find(uint id)
        {
            if (_index.ByHash(id) is { } direct && !_index.IsNested(direct))
            {
                return (direct, new MoveUnit(id, 0, null));
            }

            foreach (MoveObject state in _index.TopLevelStates)
            {
                if (MoveStateIndex.NameHashOf(state) is not { } hash)
                {
                    continue;
                }

                foreach (MoveUnit unit in MoveUnits.UnitsOf(state, hash))
                {
                    if (unit.Id == id)
                    {
                        return (state, unit);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// One row per unit: every state, plus every weapon branch group inside it. The size is the
        /// object count rather than the rendered length - a graph holds 22,000 objects, and rendering
        /// every unit just to measure it would build 25 MB of text nobody reads.
        /// </summary>
        public IReadOnlyList<FcbFragmentInfo> List()
        {
            List<FcbFragmentInfo> rows = [];
            foreach (MoveSection section in _sections)
            {
                rows.Add(new FcbFragmentInfo(
                    MoveSections.IdOf(section),
                    MoveFragmentXml.LiftSection(_index, section).Objects().Count));
            }

            foreach (MoveObject state in _index.TopLevelStates)
            {
                if (MoveStateIndex.NameHashOf(state) is not { } hash)
                {
                    continue;
                }

                IReadOnlyList<MoveUnits.Site> sites = MoveUnits.BranchesOf(state, hash);
                HashSet<MoveObject> elided = [.. sites.Select(s => s.Branch)];
                rows.Add(new FcbFragmentInfo(
                    IdOf(new MoveUnit(hash, 0, null)), Weigh(state, elided)));

                foreach (MoveUnit unit in sites.Select(s => s.Unit).Distinct())
                {
                    long size = sites.Where(s => s.Unit == unit).Sum(s => Weigh(s.Branch, []));
                    rows.Add(new FcbFragmentInfo(IdOf(unit), size));
                }
            }

            return rows;
        }

        private static long Weigh(MoveObject node, HashSet<MoveObject> stopAt)
        {
            if (stopAt.Contains(node))
            {
                return 0;
            }

            long total = 1;
            foreach (MoveOp op in node.Ops)
            {
                if (op.Kind == MoveOpKind.PointerNew)
                {
                    total += Weigh(op.Target!, stopAt);
                }
            }

            return total;
        }
    }
}
