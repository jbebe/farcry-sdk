namespace JackAll.Core.Format.Move.Rules;

/// <summary>What a rule does once the engine picks it.</summary>
public enum MoveRuleKind
{
    FullBody,
    Layered,
    Blend,
    LayeredBlend,
    Variants,
    Synced,
    Pose,
    Nothing,
    EnterState,
}

/// <summary>The part one clip plays in the rule that holds it.</summary>
public enum MoveClipRole
{
    Main,
    BlendSample,
    Variant,
    Synced,

    /// <summary>Played while leaving this rule for the link's target.</summary>
    Transition,

    /// <summary>Played while leaving any rule of an enclosing group, so shared with its siblings.</summary>
    GroupTransition,
}

/// <summary>One <c>m_animNameHash</c> a rule can play.</summary>
/// <param name="Position">Where a blend sample sits on the blend's axis.</param>
/// <param name="Link">The <c>CTransitionLink</c> a transition clip belongs to.</param>
public sealed record MoveClipSite(MoveObject Owner, MoveClipRole Role, float Position, MoveObject? Link)
{
    public uint Hash => Owner.Field("m_animNameHash") ?? 0;
}

/// <summary>
/// One node the engine can pick when it searches a state: a clip, a blend, a pose, "do nothing".
/// A state's rules in pre-order are its search order - see docs/docs/file-formats/move.md.
/// </summary>
public sealed class MoveRule
{
    /// <param name="above">Where the search started from, outermost last - for a transition's target,
    /// the objects holding the link - whose weapon pin applies when the chain has none of its own.</param>
    internal MoveRule(
        MoveObject state,
        MoveObject? entry,
        MoveObject parent,
        MoveObject node,
        IReadOnlyList<MoveObject> chain,
        int order,
        MoveRuleKind kind,
        IEnumerable<MoveObject> above)
    {
        State = state;
        Entry = entry;
        Parent = parent;
        Node = node;
        Chain = chain;
        Order = order;
        Kind = kind;
        foreach (MoveObject candidate in chain.Reverse().Concat(above))
        {
            if (MoveUnits.PinOf(candidate) is { } pin)
            {
                Pin = pin;
                PinOwner = candidate;
                break;
            }
        }

        Clips = CollectClips(node, chain);
    }

    /// <summary>The state this rule is listed under - the situation.</summary>
    public MoveObject State { get; }

    /// <summary>
    /// The <c>CTransitionLink</c> whose target this rule is searched from, or null for the state's own
    /// search. A target listed nowhere else is only ever reached by that transition.
    /// </summary>
    public MoveObject? Entry { get; }

    /// <summary>The object whose descriptor list holds <see cref="Node"/>.</summary>
    public MoveObject Parent { get; }

    public MoveObject Node { get; }

    /// <summary>Every node whose criteria gate this rule, outermost first, ending with the rule itself.</summary>
    public IReadOnlyList<MoveObject> Chain { get; }

    /// <summary>Position in the state's search order.</summary>
    public int Order { get; }

    public MoveRuleKind Kind { get; }

    /// <summary>
    /// The weapon pin governing this rule: the nearest channel 17 or 18 test above it, or above the
    /// transition link its search starts from.
    /// </summary>
    public (int Channel, int Weapon)? Pin { get; }

    public MoveObject? PinOwner { get; }

    public IReadOnlyList<MoveClipSite> Clips { get; }

    /// <summary>The descriptor-list op that holds this rule, or -1 once an edit has moved it.</summary>
    public int SlotIndex => Parent.Ops.FindIndex(op => op.Name == "CMoveDescriptor" && op.Target == Node);

    /// <summary>The state a "go to" rule continues the search in.</summary>
    public MoveObject? Target => Kind == MoveRuleKind.EnterState ? Node.FieldTarget("m_state") : null;

    /// <summary>A group on the way down has branching turned off, so the search never enters it.</summary>
    public bool IsDisabled => Chain.Any(n => n.ClassName == "CMoveGroup" && n.Field("m_branchEnable") == 0);

    internal static MoveRuleKind? KindOf(string className) => className switch
    {
        "CMoveDefParameter" or "CTimeControlledMoveParameter" => MoveRuleKind.FullBody,
        "CLayeredParameter" or "CTimeControlledLayeredParameter" => MoveRuleKind.Layered,
        "CAxialBlendAnimGroup" => MoveRuleKind.Blend,
        "CLayeredAxialBlend" => MoveRuleKind.LayeredBlend,
        "CMoveDefinition" => MoveRuleKind.Variants,
        "CSyncDefinition" or "CSyncDefParameter" => MoveRuleKind.Synced,
        "CFrankensteinParameter" => MoveRuleKind.Pose,
        "CDoNothing" => MoveRuleKind.Nothing,
        "CMoveStateRef" or "CLayeredStateRef" => MoveRuleKind.EnterState,
        _ => null,
    };

    /// <summary>Nodes the search descends through rather than stops at: groups and states.</summary>
    internal static bool IsContainer(MoveObject node)
        => node.ClassName == "CMoveGroup" || MoveStateIndex.NameHashOf(node) is not null;

    /// <summary>The children a descriptor group searches, back-references included.</summary>
    internal static IEnumerable<MoveObject> Descriptors(MoveObject node)
    {
        foreach (MoveOp op in node.Ops)
        {
            if (op.Name == "CMoveDescriptor" && op.Target is { } child)
            {
                yield return child;
            }
        }
    }

    private static List<MoveClipSite> CollectClips(MoveObject node, IReadOnlyList<MoveObject> chain)
    {
        List<MoveClipSite> sites = [];
        HashSet<MoveObject> seen = [];
        Collect(node, MoveClipRole.Main, 0, null);
        for (int i = 0; i < chain.Count - 1; i++)
        {
            CollectTransitions(chain[i], MoveClipRole.GroupTransition);
        }

        return sites;

        void Collect(MoveObject at, MoveClipRole role, float position, MoveObject? link)
        {
            if (!seen.Add(at))
            {
                return;
            }

            if (at.Field("m_animNameHash") is not null)
            {
                sites.Add(new MoveClipSite(at, role, position, link));
            }

            MoveClipRole childRole = KindOf(at.ClassName) switch
            {
                MoveRuleKind.Blend or MoveRuleKind.LayeredBlend => MoveClipRole.BlendSample,
                MoveRuleKind.Variants => MoveClipRole.Variant,
                MoveRuleKind.Synced => MoveClipRole.Synced,
                _ => role,
            };
            foreach (MoveObject child in Descriptors(at))
            {
                Collect(child, childRole, child.FieldF32("m_flAnimGroupValue") ?? 0, link);
            }

            CollectTransitions(at, MoveClipRole.Transition);
        }

        void CollectTransitions(MoveObject at, MoveClipRole role)
        {
            foreach (MoveOp op in at.Ops)
            {
                if (op.Name == "CTransitionLink" && op.Kind == MoveOpKind.PointerNew
                    && op.Target!.FieldTarget("m_group") is { } group)
                {
                    Collect(group, role, 0, op.Target);
                }
            }
        }
    }
}

/// <summary>Every state of a graph, and the rules it searches.</summary>
public sealed class MoveRuleSet
{
    private readonly Dictionary<MoveObject, IReadOnlyList<MoveRule>> _byState;

    private MoveRuleSet(IReadOnlyList<MoveObject> states, Dictionary<MoveObject, IReadOnlyList<MoveRule>> byState)
    {
        States = states;
        _byState = byState;
    }

    /// <summary>Every distinct state the machine lists, in slot order - nested ones included.</summary>
    public IReadOnlyList<MoveObject> States { get; }

    public IReadOnlyList<MoveRule> RulesOf(MoveObject state) => _byState.GetValueOrDefault(state) ?? [];

    /// <summary>The states a state's "go to" rules lead into, followed on through theirs.</summary>
    public IReadOnlyList<MoveObject> Reachable(MoveObject state)
    {
        List<MoveObject> found = [];
        HashSet<MoveObject> seen = [state];
        Queue<MoveObject> pending = new([state]);
        while (pending.TryDequeue(out MoveObject? at))
        {
            foreach (MoveRule rule in RulesOf(at))
            {
                if (rule.Target is { } next && seen.Add(next))
                {
                    found.Add(next);
                    pending.Enqueue(next);
                }
            }
        }

        return found;
    }

    public static MoveRuleSet Build(MoveStateIndex index)
    {
        List<MoveObject> states = [.. index.Slots.Distinct()];
        Dictionary<MoveObject, IReadOnlyList<MoveRule>> byState = [];
        foreach (MoveObject state in states)
        {
            byState[state] = Walk(index, state);
        }

        return new MoveRuleSet(states, byState);
    }

    /// <summary>
    /// The state's own search, then one search from every transition target it cannot otherwise reach.
    /// </summary>
    private static List<MoveRule> Walk(MoveStateIndex index, MoveObject state)
    {
        List<MoveRule> rules = [];
        List<MoveObject> chain = [];
        HashSet<MoveObject> reached = [state];
        HashSet<MoveObject> searched = [state];
        List<MoveObject> above = [];
        Visit(state, null);

        // A nested state entered through a "go to" rule keeps its transitions to its own list.
        List<MoveObject> links = [.. state.Subtree().Where(o => o.ClassName == "CTransitionLink")];
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (MoveObject link in links)
            {
                if (EnclosingState(link) is not { } home || !searched.Contains(home)
                    || link.FieldTarget("m_ptr") is not { } target || !reached.Add(target))
                {
                    continue;
                }

                grew = true;
                above.Clear();
                for (MoveObject? holder = index.OwnerOf(link); holder is not null;
                     holder = holder == state ? null : index.OwnerOf(holder))
                {
                    above.Add(holder);
                }

                if (MoveRule.KindOf(target.ClassName) is { } kind)
                {
                    Reach(target);
                    rules.Add(new MoveRule(state, link, link, target, [target], rules.Count, kind, above));
                }
                else if (MoveRule.IsContainer(target))
                {
                    Visit(target, link);
                }
            }
        }

        return rules;

        void Visit(MoveObject parent, MoveObject? entry)
        {
            foreach (MoveObject child in MoveRule.Descriptors(parent))
            {
                if (chain.Contains(child))
                {
                    continue;
                }

                reached.Add(child);
                chain.Add(child);
                if (MoveRule.KindOf(child.ClassName) is { } kind)
                {
                    Reach(child);
                    rules.Add(new MoveRule(state, entry, parent, child, [.. chain], rules.Count, kind, above));
                }
                else if (MoveRule.IsContainer(child))
                {
                    if (MoveStateIndex.NameHashOf(child) is not null)
                    {
                        searched.Add(child);
                    }

                    Visit(child, entry);
                }

                chain.RemoveAt(chain.Count - 1);
            }
        }

        MoveObject? EnclosingState(MoveObject obj)
        {
            for (MoveObject? at = index.OwnerOf(obj); at is not null; at = index.OwnerOf(at))
            {
                if (MoveStateIndex.NameHashOf(at) is not null)
                {
                    return at;
                }
            }

            return null;
        }

        // A rule's own samples and variants are played through it, never searched on their own.
        void Reach(MoveObject rule)
        {
            foreach (MoveObject inside in MoveRule.Descriptors(rule))
            {
                if (reached.Add(inside))
                {
                    Reach(inside);
                }
            }
        }
    }
}
