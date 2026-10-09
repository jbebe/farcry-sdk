namespace JackAll.Tools.Ai;

/// <summary>How the AI fires each weapon: the <c>AIShootingSystem</c> block of its properties archetype.</summary>
public static class WeaponFields
{
    private const string Misses = "Forced misses after a hit";
    private const string Bursts = "Bursts (read from the names; engine use not traced)";

    private static readonly string[] Shooting = ["CWeaponProperties", "CommonProperties", "AIShootingSystem"];

    private static readonly (string Label, string Suffix)[] Difficulties =
        [.. Difficulty.Names.Zip(["Causal", "Experimented", "Hardcore", "Infamous"])];

    public static IReadOnlyList<TuningField> All { get; } =
    [
        .. Difficulties.SelectMany(d => (TuningField[])
        [
            new(Misses, $"{d.Label}: at least", $"After a soldier lands a hit on the player with this weapon on {d.Label}, he misses at least this many shots in a row. The main lever against runs of hits; 0 lets hits chain freely.", Shooting, "nForcedFailureMin" + d.Suffix, TuningFieldKind.Whole),
            new(Misses, $"{d.Label}: at most", $"The most shots he misses in a row after a hit on {d.Label}; the count is drawn between the two.", Shooting, "nForcedFailureMax" + d.Suffix, TuningFieldKind.Whole),
        ]),
        .. Range("Long", "long"),
        .. Range("Mid", "medium"),
        .. Range("Short", "short"),
        new(Bursts, "Hit streak timer (s)", "Timer that goes with the weapon's successful-hit curve. How the engine uses it is not traced yet.", Shooting, "fSuccessfulBulletHitTimer"),
    ];

    /// <summary>Every weapon properties archetype, grouped by slot; multiplayer variants under their own heading.</summary>
    public static TuningCatalog Catalog { get; } = new(All, All[0], name => name.EndsWith(".Multi", StringComparison.Ordinal)
        ? "Multiplayer"
        : name.Split('.') is [_, string slot, ..] ? slot : name);

    private static IEnumerable<TuningField> Range(string key, string range)
    {
        yield return new(Bursts, $"Burst at {range} range: shortest (s)", $"Shortest trigger pull at {range} range.", Shooting, $"fBurstLength{key}_Min");
        yield return new(Bursts, $"Burst at {range} range: longest (s)", $"Longest trigger pull at {range} range.", Shooting, $"fBurstLength{key}_Max");
        yield return new(Bursts, $"Pause at {range} range: shortest (s)", $"Shortest pause between bursts at {range} range. Longer pauses give the player windows to move.", Shooting, $"fBurstWait{key}_Min");
        yield return new(Bursts, $"Pause at {range} range: longest (s)", $"Longest pause between bursts at {range} range.", Shooting, $"fBurstWait{key}_Max");
    }
}
