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
public sealed class MoveContainerSplitter(MoveNames? names = null) : IContainerSplitter
{
    /// <summary>
    /// The graphs that split. The `*named.bin` twins are deliberately absent: they set
    /// <c>dwFileFormat &amp; 0x20000</c>, which <c>CMoveMgr::CreateFromStream</c> rejects outright, so
    /// they are authoring artifacts no engine will load.
    /// </summary>
    private static readonly string[] Graphs = ["movemgr.bin", "dlc1.bin"];

    /// <summary>
    /// The nameless one, for a caller with no name table to hand. It lists fragments under a bare
    /// <c>state_&lt;hex&gt;</c> label, which compares equal to any other spelling of the same number,
    /// so a build never needs names.
    /// </summary>
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
    /// <param name="stateName">
    /// The state's own name when one is known. It is the <em>stem</em> of the label, not the whole of
    /// it: a weapon branch keeps its <c>_ch17_w39</c> tail, because that is the only thing separating
    /// two units of one state.
    /// </param>
    public static string IdOf(MoveUnit unit, string? stateName = null)
        => FragmentId.Of(unit.Id, unit.LabelFor(stateName));

    /// <summary>The unit a fragment id names - see <see cref="FragmentId.NumberOf"/>.</summary>
    public static uint? UnitOf(string fragmentId) => FragmentId.NumberOf(fragmentId);

    public IContainerTree Open(byte[] container) => new Tree(MoveCodec.Load(container), names);

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
            if (FragmentId.NumberOf(id) != unitId)
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

    private sealed class Tree : IContainerTree
    {
        private readonly MoveStateIndex _index;
        private readonly MoveNames _names;

        /// <summary>
        /// Which manager sections this graph has - none for an expansion, which is a bare
        /// <c>CMoveStateMachine</c> with no manager and no value container at all.
        /// </summary>
        private readonly IReadOnlyCollection<MoveSection> _sections;

        private readonly Dictionary<uint, (MoveObject State, MoveUnit Unit)> _units;

        /// <summary>Built here rather than on demand: a build hands one tree to every fragment of a
        /// container at once, in parallel, so nothing about it may be populated late.</summary>
        public Tree(MoveFile file, MoveNames? names)
        {
            _index = MoveStateIndex.Build(file);
            _names = names ?? MoveNames.Empty;
            _sections = MoveSections.Ranges(file) is { } ranges ? [.. ranges.Keys] : [];
            _units = Catalogue();
        }

        public string? Extract(string fragmentId)
        {
            // Reserved names are matched first, so a state can never shadow a section.
            if (MoveSections.Parse(fragmentId) is { } section)
            {
                return _sections.Contains(section)
                    ? MoveFragmentXml.Render(MoveFragmentXml.LiftSection(_index, section))
                    : null;
            }

            if (FragmentId.NumberOf(fragmentId) is not { } id
                || !_units.TryGetValue(id, out (MoveObject state, MoveUnit unit) found))
            {
                return null;
            }

            (MoveObject state, MoveUnit unit) = found;

            return MoveFragmentXml.Render(unit.IsRemainder
                ? MoveFragmentXml.LiftState(_index, state)
                : MoveFragmentXml.LiftBranches(_index, state, unit));
        }

        /// <summary>
        /// Every unit this graph holds, by the number a fragment id resolves to. Indexed up front
        /// because an import asks one container for all 2,312 of its fragments, and a branch id
        /// never short-circuits on <see cref="MoveStateIndex.ByHash"/> - it would re-walk every
        /// state's subtree per lookup.
        /// </summary>
        /// <remarks>
        /// Branches go in first so a state overwrites one that collides with it, keeping the
        /// precedence the search this replaced had: a state wins over a branch of the same number.
        /// </remarks>
        private Dictionary<uint, (MoveObject State, MoveUnit Unit)> Catalogue()
        {
            Dictionary<uint, (MoveObject, MoveUnit)> units = [];
            foreach (MoveObject state in _index.TopLevelStates)
            {
                if (MoveStateIndex.NameHashOf(state) is not { } hash)
                {
                    continue;
                }

                foreach (MoveUnit unit in MoveUnits.UnitsOf(state, hash).Where(u => !u.IsRemainder))
                {
                    units[unit.Id] = (state, unit);
                }
            }

            foreach (MoveObject state in _index.TopLevelStates)
            {
                if (MoveStateIndex.NameHashOf(state) is { } hash)
                {
                    units[hash] = (state, new MoveUnit(hash, 0, null));
                }
            }

            return units;
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

                string? name = _names.Of(hash);
                IReadOnlyList<MoveUnits.Site> sites = MoveUnits.BranchesOf(state, hash);
                HashSet<MoveObject> elided = [.. sites.Select(s => s.Branch)];
                rows.Add(new FcbFragmentInfo(
                    IdOf(new MoveUnit(hash, 0, null), name), Weigh(state, elided)));

                foreach (MoveUnit unit in sites.Select(s => s.Unit).Distinct())
                {
                    long size = sites.Where(s => s.Unit == unit).Sum(s => Weigh(s.Branch, []));
                    rows.Add(new FcbFragmentInfo(IdOf(unit, name), size));
                }
            }

            return rows;
        }

        /// <summary>
        /// The graph with every listed state's subtree and every manager section replaced by a
        /// marker, rendered through <see cref="MoveXml.ToXml"/> so the header, class names, op names
        /// and value encodings all come along - see <see cref="IContainerTree.Skeleton"/>.
        /// </summary>
        /// <remarks>
        /// Section markers and the ops they hide come from one <see cref="MoveSections.Ranges"/> call,
        /// so a section's content can never end up counted as residue. <c>nbState</c> is dropped
        /// because <see cref="Rebuild"/> re-derives it from the slot count, and keeping it would make
        /// every added state fail a comparison additions are supposed to pass.
        /// </remarks>
        public string? Skeleton(Func<string, bool> keep)
        {
            MoveFile file = _index.File;
            MoveObject? manager = file.Objects.FirstOrDefault(o => o.ClassName == "CMoveMgr");
            IReadOnlyDictionary<MoveSection, (int Start, int Count)> ranges =
                manager is not null ? MoveSections.Ranges(manager) : new Dictionary<MoveSection, (int, int)>();

            Dictionary<MoveObject, MoveObject> clones = new(ReferenceEqualityComparer.Instance);
            List<(MoveObject Owner, int At, MoveObject Target)> refs = [];
            var skeleton = new MoveFile { Type = file.Type, Version = file.Version, Flags = file.Flags };
            skeleton.Root = Prune(file.Root);

            foreach ((MoveObject owner, int at, MoveObject target) in refs)
            {
                string name = owner.Ops[at].Name;
                owner.Ops[at] = clones.TryGetValue(target, out MoveObject? cloned)
                    ? MoveOp.Pointer(MoveOpKind.PointerRef, name, cloned)
                    : Marker(name, _index.AddressOf(target)?.ToString() ?? target.ClassName);
            }

            // ToXml addresses an object by its index, so the pruned tree needs its own numbering:
            // two skeletons of the same shape have to render the same ids.
            skeleton.Reindex();
            for (int i = 0; i < skeleton.Objects.Count; i++)
            {
                skeleton.Objects[i].Index = i;
            }

            return MoveXml.ToXml(skeleton);

            MoveObject Prune(MoveObject node)
            {
                var clone = new MoveObject(node.ClassName);
                clones[node] = clone;
                bool isManager = ReferenceEquals(node, manager);

                for (int i = 0; i < node.Ops.Count; i++)
                {
                    if (isManager && SectionAt(ranges, i) is { } section)
                    {
                        clone.Ops.Add(Marker("#section", MoveSections.IdOf(section)));
                        i += ranges[section].Count - 1;
                        continue;
                    }

                    MoveOp op = node.Ops[i];
                    switch (op.Kind)
                    {
                        case MoveOpKind.PointerNew when FragmentIdOf(op.Target!) is { } id:
                            if (keep(id))
                            {
                                clone.Ops.Add(Marker(op.Name, id));
                            }
                            break;
                        case MoveOpKind.PointerNew:
                            clone.Ops.Add(MoveOp.Pointer(MoveOpKind.PointerNew, op.Name, Prune(op.Target!)));
                            break;
                        case MoveOpKind.PointerRef:
                            refs.Add((clone, clone.Ops.Count, op.Target!));
                            clone.Ops.Add(op);
                            break;
                        default:
                            if (op.Name != "nbState")
                            {
                                clone.Ops.Add(op);
                            }
                            break;
                    }
                }

                return clone;
            }
        }

        /// <summary>The fragment id a state is listed under, or null when it is not one - a nested
        /// state, or a top-level one with no <c>m_stateNameHash</c>, which <see cref="List"/> emits
        /// no row for and so has to stay in the skeleton as residue.</summary>
        private string? FragmentIdOf(MoveObject node)
            => ReferenceEquals(_index.StateOf(node), node) && MoveStateIndex.NameHashOf(node) is { } hash
                ? FragmentId.Of(hash)
                : null;

        private static MoveSection? SectionAt(
            IReadOnlyDictionary<MoveSection, (int Start, int Count)> ranges, int op)
        {
            foreach ((MoveSection section, (int start, _)) in ranges)
            {
                if (start == op)
                {
                    return section;
                }
            }

            return null;
        }

        private static MoveOp Marker(string name, string text)
            => MoveOp.Blob(MoveOpKind.Str, name, Encoding.ASCII.GetBytes(text));

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
