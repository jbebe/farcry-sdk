using System.Diagnostics.CodeAnalysis;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Mods;

namespace JackAll.Core.Format.Move.Rules;

/// <summary>One fragment a save has to stage.</summary>
/// <param name="IsVanilla">Its text is back to the retail graph's, so an override of it can go.</param>
public sealed record MoveFragmentChange(string Id, string Xml, bool IsVanilla);

/// <summary>What saving the session means: these fragments, which together rebuild <see cref="Result"/>.</summary>
public sealed record MoveSavePlan(IReadOnlyList<MoveFragmentChange> Changes, byte[] Result)
{
    /// <summary>The result, already split, so committing it costs nothing.</summary>
    internal IContainerTree? Tree { get; init; }
}

/// <summary>
/// A MOVE graph open for editing as rules. Its baseline is the graph as loaded, every enabled mod
/// applied, so a save stages this session's edits and never another mod's fragments.
/// </summary>
public sealed class MoveEditSession
{
    private readonly MoveContainerSplitter _splitter;
    private readonly byte[]? _vanillaBytes;
    private byte[] _baselineBytes;
    private IContainerTree? _baseline;
    private IContainerTree? _vanilla;

    /// <param name="graph">The graph as the game would load it now.</param>
    /// <param name="vanilla">The retail graph, or null for one a mod added.</param>
    /// <param name="named">The channel table from <c>movemgrnamed.bin</c>, when it is at hand.</param>
    public MoveEditSession(byte[] graph, byte[]? vanilla, MoveNames names, IReadOnlyList<MoveChannel>? named)
    {
        _splitter = new MoveContainerSplitter(names);
        _vanillaBytes = vanilla;
        _baselineBytes = graph;
        Names = names;
        File = MoveCodec.Load(graph);
        Channels = MoveChannels.From(named, File);
        Rebuild();
    }

    public MoveFile File { get; private set; }

    public MoveStateIndex Index { get; private set; }

    public MoveRuleSet Rules { get; private set; }

    public MoveChannels Channels { get; }

    public MoveNames Names { get; }

    public bool IsDirty { get; private set; }

    public string NameOf(MoveObject state)
        => MoveStateIndex.NameHashOf(state) is { } hash
            ? new MoveUnit(hash, 0, null).LabelFor(Names.Of(hash))
            : state.ClassName;

    public void SetClip(MoveObject site, uint hash) => SetValue(site, () => site.SetField("m_animNameHash", hash));

    public void SetFloat(MoveObject owner, string field, float value)
        => SetValue(owner, () => owner.SetFieldF32(field, value));

    public void SetNumber(MoveObject owner, string field, uint value)
        => SetValue(owner, () => owner.SetField(field, value));

    public void SetCondition(MoveObject owner, MoveCondition condition, MoveConditionSpec spec)
    {
        GuardPin(owner, condition.Criterion);
        bool weaponTest = IsWeaponChannel(condition.Channel) || IsWeaponChannel(spec.Channel);
        if (!Guarded(() => MoveCriteria.TryWrite(condition.Criterion, spec, Channels.TypeOf(spec.Channel))))
        {
            MoveObject replacement = Build(spec);
            Structural(() =>
            {
                int at = owner.Ops.FindIndex(op => op.Target == condition.Criterion);
                owner.Ops[at] = owner.Ops[at].WithTarget(replacement);
            });
        }
        else if (weaponTest)
        {
            // A rewritten weapon test can move a rule to another weapon's branch.
            Rebuild();
        }

        IsDirty = true;
    }

    public void AddCondition(MoveObject owner, MoveConditionSpec spec)
    {
        MoveObject criterion = Build(spec);
        Structural(() =>
        {
            int end = owner.Ops.FindIndex(op => op.Name == "CMoveCriteria" && op.Kind == MoveOpKind.PointerNull);
            owner.Ops.Insert(end, MoveOp.Pointer(MoveOpKind.PointerNew, "CMoveCriteria", criterion));
        });
    }

    public void RemoveCondition(MoveObject owner, MoveCondition condition)
    {
        GuardPin(owner, condition.Criterion);
        Structural(() => owner.Ops.RemoveAll(op => op.Target == condition.Criterion));
    }

    /// <summary>Copies a rule into the slot just ahead of it, so the copy is tried first.</summary>
    public MoveObject Duplicate(MoveRule rule)
    {
        int slot = SlotOf(rule);
        MoveObject copy = MoveEdits.DeepCopy(rule.Node);
        Structural(() => rule.Parent.Ops.Insert(slot, MoveOp.Pointer(MoveOpKind.PointerNew, "CMoveDescriptor", copy)));
        return copy;
    }

    public void Delete(MoveRule rule)
    {
        int slot = SlotOf(rule);
        if (rule.Parent.Ops[slot].Kind == MoveOpKind.PointerNew)
        {
            GuardDelete(rule);
        }

        Structural(() => rule.Parent.Ops.RemoveAt(slot));
    }

    /// <summary>Moves a rule earlier (negative) or later in its group's search order.</summary>
    public void Move(MoveRule rule, int delta)
    {
        List<int> slots = [.. Enumerable.Range(0, rule.Parent.Ops.Count)
            .Where(i => rule.Parent.Ops[i].Name == "CMoveDescriptor" && rule.Parent.Ops[i].Target is not null)];
        int from = slots.IndexOf(SlotOf(rule));
        int to = from + delta;
        if (to < 0 || to >= slots.Count)
        {
            throw new MoveEditException(delta < 0
                ? "This is already the first thing its group tries."
                : "This is already the last thing its group tries.");
        }

        Structural(() =>
        {
            List<MoveOp> ops = rule.Parent.Ops;
            (ops[slots[from]], ops[slots[to]]) = (ops[slots[to]], ops[slots[from]]);
        });
    }

    /// <summary>
    /// Gives weapon index <paramref name="target"/> a copy of every branch <paramref name="donor"/>
    /// has, optionally moving the copies' clips into a newly registered <paramref name="package"/>.
    /// </summary>
    public MoveCloneResult CloneWeapon(int donor, int target, string? package)
    {
        MoveCloneResult? result = null;
        Structural(() => result = MoveEdits.CloneWeapon(File, Index, Names, donor, target, package));
        return result!;
    }

    /// <summary>
    /// Every fragment that differs from the baseline, checked by rebuilding the edited graph from them:
    /// if the rebuild is not byte-identical, nothing is staged.
    /// </summary>
    public MoveSavePlan Plan()
    {
        byte[] edited = MoveCodec.Save(File);
        IContainerTree mine = _splitter.Open(MoveCodec.Load(edited));
        IContainerTree baseline = _baseline ??= _splitter.Open(_baselineBytes);
        HashSet<string> ids = new(FcbFragments.IdComparer);
        ids.UnionWith(mine.List().Select(r => r.Id));
        ids.UnionWith(baseline.List().Select(r => r.Id));

        IReadOnlyList<FragmentChange> changed = Guarded(() => FragmentDiff.Changed(mine, baseline, ids));
        byte[] rebuilt = Guarded(() => _splitter.Apply(_baselineBytes, changed.ToDictionary(c => c.Id, c => c.Xml)));
        if (!rebuilt.AsSpan().SequenceEqual(edited))
        {
            throw new MoveEditException(
                "These edits do not survive being split into mod fragments: rebuilding the graph from "
                + "them gives a different file. Nothing was staged.");
        }

        _vanilla ??= _vanillaBytes is null ? null : _splitter.Open(_vanillaBytes);
        return new MoveSavePlan(
            [.. changed.Select(c => new MoveFragmentChange(c.Id, c.Xml, _vanilla?.Extract(c.Id) == c.Xml))],
            edited)
        {
            Tree = mine,
        };
    }

    /// <summary>The plan's fragments are staged: its result is what the game now loads.</summary>
    public void Commit(MoveSavePlan plan)
    {
        _baselineBytes = plan.Result;
        _baseline = plan.Tree;
        IsDirty = false;
    }

    /// <summary>Drops every unsaved edit.</summary>
    public void Revert()
    {
        File = MoveCodec.Load(_baselineBytes);
        Rebuild();
        IsDirty = false;
    }

    private static bool IsWeaponChannel(int channel)
        => channel is MoveWeapons.EquippedWeaponChannel or MoveWeapons.DesiredWeaponChannel;

    private MoveObject Build(MoveConditionSpec spec)
    {
        MoveValueType type = Channels.TypeOf(spec.Channel);
        if (type == MoveValueType.Unknown)
        {
            throw new MoveEditException(
                $"What {Channels.NameOf(spec.Channel)} holds is unknown without movemgrnamed.bin, so no condition can be built on it.");
        }

        return Guarded(() => MoveCriteria.Build(spec, type));
    }

    /// <summary>Refuses to change the test that pins a branch to its weapon.</summary>
    private void GuardPin(MoveObject owner, MoveObject criterion)
    {
        if (MoveUnits.PinCriterionOf(owner) == criterion && MoveUnits.PinOf(owner) is { } pin)
        {
            throw new MoveEditException(
                $"This condition is what makes it the {Channels.Format(pin.Channel, pin.Weapon)} branch. "
                + "To give another weapon these animations, copy the weapon's set instead.");
        }
    }

    private void GuardDelete(MoveRule rule)
    {
        HashSet<MoveObject> owned = [.. rule.Node.Subtree()];
        if (MoveEdits.HoldsState(rule.Node))
        {
            throw new MoveEditException("This rule holds a nested state, which other states name. It cannot be deleted.");
        }

        foreach (MoveObject obj in File.Objects.Where(o => !owned.Contains(o)))
        {
            if (obj.Ops.Any(op => op.Kind == MoveOpKind.PointerRef && owned.Contains(op.Target!)))
            {
                string where = Index.StateOf(obj) is { } state ? NameOf(state) : "the manager";
                throw new MoveEditException(
                    $"A {obj.ClassName} in {where} refers to this rule, so deleting it would break that reference.");
            }
        }

        if (MoveUnits.PinOf(rule.Node) is not null
            && Index.StateOf(rule.Node) is { } top && MoveStateIndex.NameHashOf(top) is { } hash)
        {
            IReadOnlyList<MoveUnits.Site> sites = MoveUnits.BranchesOf(top, hash);
            if (sites.FirstOrDefault(s => s.Branch == rule.Node) is { Branch: not null } site
                && sites.Count(s => s.Unit == site.Unit) == 1)
            {
                throw new MoveEditException(
                    "This is the weapon's only branch in its state, and a mod fragment cannot remove a "
                    + "branch outright. Delete the rules inside it, or change its clip.");
            }
        }
    }

    private static int SlotOf(MoveRule rule)
    {
        if (rule.SlotIndex is >= 0 and var slot)
        {
            return slot;
        }

        throw new MoveEditException(rule.Parent == rule.Entry
            ? "This rule is where a transition lands rather than an entry in a list, so it cannot be copied, moved or deleted."
            : "That rule has moved since it was shown; pick it again.");
    }

    private void SetValue(MoveObject owner, Func<bool> write)
    {
        if (!write())
        {
            throw new MoveEditException($"A {owner.ClassName} has no such field.");
        }

        IsDirty = true;
    }

    /// <summary>Runs an edit that changes op lists, and undoes it when the graph can no longer be written.</summary>
    private void Structural(Action edit)
    {
        Dictionary<MoveObject, List<MoveOp>> before = File.Objects.ToDictionary(o => o, o => new List<MoveOp>(o.Ops));
        try
        {
            edit();
            File.Reindex();
            MoveCodec.Save(File);
        }
        catch (Exception ex) when (ex is MoveFormatException or MoveEditException)
        {
            foreach ((MoveObject obj, List<MoveOp> ops) in before)
            {
                obj.Ops.Clear();
                obj.Ops.AddRange(ops);
            }

            File.Reindex();
            throw ex as MoveEditException ?? new MoveEditException($"The graph cannot store that change: {ex.Message}");
        }

        Rebuild();
        IsDirty = true;
    }

    [MemberNotNull(nameof(Index), nameof(Rules))]
    private void Rebuild()
    {
        Index = MoveStateIndex.Build(File);
        Rules = MoveRuleSet.Build(Index);
    }

    /// <summary>A format refusal, rethrown as the edit refusal callers catch.</summary>
    private static T Guarded<T>(Func<T> write)
    {
        try
        {
            return write();
        }
        catch (Exception ex) when (ex is MoveFormatException or InvalidDataException)
        {
            throw new MoveEditException(ex.Message);
        }
    }
}
