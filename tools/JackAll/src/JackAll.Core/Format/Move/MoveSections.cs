namespace JackAll.Core.Format.Move;

/// <summary>The parts of a MOVE graph that belong to the manager rather than to any state.</summary>
public enum MoveSection
{
    /// <summary>The 105 value channels the whole graph is indexed by.</summary>
    Channels,

    /// <summary>The animation package names - what a new weapon has to be registered in.</summary>
    Packages,

    /// <summary>The blend set, its categories and their poses.</summary>
    BlendSets,

    /// <summary>The default transition and the category x category transition matrix.</summary>
    Transitions,
}

/// <summary>
/// Where each manager section sits in <c>CMoveMgr</c>'s own op list.
/// </summary>
/// <remarks>
/// <c>CMoveMgr::Serialize</c> writes these inline rather than as separate objects, so a section is a
/// contiguous run of ops rather than a subtree. The runs are found by the field names the engine's
/// own <c>Transfer</c> calls pass, which is the same vocabulary the XML uses.
///
/// They are four rather than one because a single combined manager fragment is around 38 KB of XML,
/// over `mod-layout-final.md`'s ~20 KB line, and because splitting on these seams means the fragment
/// a mod actually edits - the package list - carries no pointers at all. See
/// docs/docs/file-formats/move.md.
/// </remarks>
public static class MoveSections
{
    /// <summary>The reserved fragment id each section is staged under.</summary>
    public static string IdOf(MoveSection section) => section switch
    {
        MoveSection.Channels => "_channels.xml",
        MoveSection.Packages => "_packages.xml",
        MoveSection.BlendSets => "_blendsets.xml",
        MoveSection.Transitions => "_transitions.xml",
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    /// <summary>The section a fragment id names, or null when it names a state or a branch.</summary>
    public static MoveSection? Parse(string fragmentId)
    {
        foreach (MoveSection section in Enum.GetValues<MoveSection>())
        {
            if (string.Equals(fragmentId, IdOf(section), StringComparison.OrdinalIgnoreCase))
            {
                return section;
            }
        }

        return null;
    }

    /// <summary>The name the XML carries, and what <see cref="Parse(string)"/> reads back.</summary>
    public static string NameOf(MoveSection section) => section.ToString().ToLowerInvariant();

    public static MoveSection? ByName(string name)
    {
        foreach (MoveSection section in Enum.GetValues<MoveSection>())
        {
            if (string.Equals(name, NameOf(section), StringComparison.OrdinalIgnoreCase))
            {
                return section;
            }
        }

        return null;
    }

    /// <summary>
    /// Where each section starts and how many ops it covers, or null when this graph has no manager -
    /// an expansion like <c>dlc1.bin</c> is a bare state machine and has none of these.
    /// </summary>
    public static IReadOnlyDictionary<MoveSection, (int Start, int Count)>? Ranges(MoveFile file)
    {
        MoveObject? manager = file.Objects.FirstOrDefault(o => o.ClassName == "CMoveMgr");
        return manager is null ? null : Ranges(manager);
    }

    public static IReadOnlyDictionary<MoveSection, (int Start, int Count)> Ranges(MoveObject manager)
    {
        int channels = IndexOf(manager, "CMoveValueContainer");
        int packages = IndexOf(manager, "PackageList");
        int machine = IndexOf(manager, "CMoveStateMachine");

        // The TransitionFile stamp is written only at CMoveMgr version > 4; without it the blend sets
        // begin at their own count. Both shipped graphs are version 5, so the fallback is untested
        // rather than dead.
        int blendSets = IndexOf(manager, "TransitionFile");
        if (blendSets < 0)
        {
            blendSets = IndexOf(manager, "m_iNumMoveBlendSet");
        }

        int transitions = IndexOf(manager, "m_defaultTransition");
        if (transitions < 0)
        {
            transitions = IndexOf(manager, "m_transitionMatrix");
        }

        if (channels < 0 || packages < 0 || blendSets < 0 || machine < 0)
        {
            throw new MoveFormatException("this CMoveMgr does not have the shape the sections assume");
        }

        Dictionary<MoveSection, (int, int)> ranges = new()
        {
            [MoveSection.Channels] = (channels, 1),
            [MoveSection.Packages] = (packages, blendSets - packages),
            [MoveSection.BlendSets] = (blendSets, machine - blendSets),
        };

        if (transitions >= 0)
        {
            ranges[MoveSection.Transitions] = (transitions, manager.Ops.Count - transitions);
        }

        return ranges;
    }

    private static int IndexOf(MoveObject manager, string name)
    {
        for (int i = 0; i < manager.Ops.Count; i++)
        {
            if (manager.Ops[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The channel count a <see cref="MoveSection.Channels"/> fragment declares.
    /// </summary>
    /// <remarks>
    /// <c>MSAnim::LoadMoves</c> compares this against a hardcoded 105 and <em>drops the file</em>
    /// otherwise, so a graph that declares anything else loads as no animation at all, silently. That
    /// is the worst failure available and the cheapest to refuse up front.
    /// </remarks>
    public const int RequiredChannelCount = 105;

    public static uint? DeclaredChannelCount(IReadOnlyList<MoveOp> ops)
    {
        foreach (MoveOp op in ops)
        {
            if (op.Kind == MoveOpKind.PointerNew && op.Target is { } container)
            {
                return container.Field("ms_iNumMoveValue");
            }
        }

        return null;
    }
}
