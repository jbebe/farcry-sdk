namespace JackAll.Tools.Ai;

/// <summary>A value a parameter can take, with the name the editor shows for it.</summary>
public sealed record AiChoice(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Names for the values brain parameters take: the 64 agent flags, named after the tasks that set and
/// test them (and, where <c>CBrainMercCombat::DoStart</c> acts on one, the behaviour it requests),
/// the flag operations, and yes/no for every bool the engine reads.
/// </summary>
public static class AiFlags
{
    public static IReadOnlyDictionary<int, string> Names { get; } = new Dictionary<int, string>
    {
        [0] = "Social reaction (bump, charge plans)",
        [2] = "Under water",
        [3] = "Fire",
        [4] = "Use vehicle",
        [6] = "Fall to death",
        [7] = "Has been in combat",
        [8] = "Social combat",
        [9] = "Is suppressing",
        [10] = "Don't reload",
        [11] = "Switch to alert",
        [14] = "Shoot flare → combat: ShootFlare",
        [15] = "New best target → combat: SelectBestTarget",
        [16] = "Projectile seen → combat: GrenadeEscape",
        [17] = "Fire is near → combat: SwitchToFireAlert",
        [18] = "Visual threat lost",
        [19] = "Incoming vehicle → combat: EscapeVehicle",
        [20] = "Grenade attack request",
        [21] = "Switch to fire alert",
        [22] = "Target only the player",
        [23] = "I am dead",
        [24] = "Is hurt",
        [25] = "Play hurt → combat: MercBhvHurt",
        [27] = "Is alerted",
        [28] = "Something seen",
        [29] = "Interested",
        [30] = "Target is safe",
        [32] = "Using mounted weapon → combat: MountedWeapon",
        [33] = "Keep mounted weapon",
        [36] = "Corpse seen",
        [37] = "Driver not alone",
        [38] = "Friend died",
        [40] = "Flamethrower close",
        [41] = "Flamethrower far",
        [42] = "Last man standing",
        [43] = "Rush target → combat: RushTarget",
        [44] = "Formation point changed",
        [45] = "Need to relocate → combat: Relocate",
        [46] = "Dynamic-zone combat",
        [47] = "Silent sniper alert",
        [48] = "Is under fire",
        [49] = "Shot blocked by a friend",
        [51] = "AI versus AI",
        [52] = "Vehicle detach mode",
        [53] = "Rush clash point → combat: RushClashPoint",
        [54] = "Look along path",
        [55] = "No cover, run to threat → combat: NoCoverRunToThreat",
        [56] = "Target is higher → combat: HigherTarget",
        [57] = "Fall back → combat: FallBackToStartPosition",
        [58] = "Fallback complete",
        [59] = "Incoming friendly vehicle → combat: EscapeFriendlyVehicle",
        [61] = "Scripted shoot at target",
        [62] = "Hide from far shooter",
        [63] = "Shot by another target → combat: ShotByAnotherTarget",
    };

    private static readonly AiChoice[] Flags =
        [.. Enumerable.Range(0, 64).Select(i => new AiChoice($"{i}", Names.TryGetValue(i, out string? n) ? $"{i} - {n}" : $"{i}"))];

    private static readonly AiChoice[] Operations = [new("0", "Set"), new("1", "Clear"), new("2", "Check (fires IS_TRUE / IS_FALSE)")];

    private static readonly AiChoice[] FlagOwners = [new("0", "This soldier's flags"), new("1", "His army's flags")];

    private static readonly AiChoice[] YesNo = [new("0", "No"), new("1", "Yes")];

    /// <summary>The named values of one parameter, or null when it is free-form.</summary>
    public static IReadOnlyList<AiChoice>? ChoicesFor(string cls, string parameter, string? type) => parameter switch
    {
        "FlagFieldValue" => Flags,
        "OperationType" when cls == "CTaskOperateOnFlagField" => Operations,
        "FlagFieldType" => FlagOwners,
        _ when type == "bool" => YesNo,
        _ => null,
    };
}
