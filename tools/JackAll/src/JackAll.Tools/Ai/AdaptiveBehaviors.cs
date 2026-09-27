using System.Globalization;
using System.Text.RegularExpressions;

namespace JackAll.Tools.Ai;

/// <summary>One adaptive behaviour: its chance in percent at each of the 28 progression levels.</summary>
public sealed record AdaptiveBehavior(string Name, double[] Chances);

/// <summary>
/// The <c>AdaptativeBehavior</c> table of <c>engine\gamemodes\gamemodesconfig.xml</c>: how likely each
/// optional behaviour is, per progression level - the Weapons service's level, the same 0-27 scale the
/// enemy weapon packs use. Edits rewrite only the numbers, so the rest of the file keeps its layout.
/// </summary>
public static partial class AdaptiveBehaviors
{
    public const string Path = @"engine\gamemodes\gamemodesconfig.xml";

    public const int Levels = 28;

    /// <summary>What each behaviour gates, as far as it is known.</summary>
    public static IReadOnlyDictionary<string, string> Descriptions { get; } = new Dictionary<string, string>
    {
        ["Grenade"] = "Throws a grenade at the player in the open.",
        ["GrenadeAndBuilding"] = "Throws a grenade into the building the player hides in.",
        ["ChaseWithVehicle"] = "Chases the player by car after he drives through a checkpoint.",
        ["ReachSniperWithVehicle"] = "Drives towards a player who snipes from far away.",
        ["MountedWeapon"] = "Mans a mounted gun.",
        ["ShootFlare"] = "Fires a flare to call reinforcements.",
        ["ShootInterestingObject"] = "Shoots explosive barrels and similar objects near the player.",
        ["RescueVictim"] = "Drags a wounded friend to cover and heals him.",
        ["RangeWeapon"] = "Mortar crews fire a ranging smoke shell before the explosive one.",
        ["VehicleChaseLevel2"] = "Vehicle chase, second escalation.",
        ["VehicleChaseLevel3"] = "Vehicle chase, third escalation.",
        ["LongRangeVehicle"] = "Engages from vehicles at long range.",
    };

    public static IReadOnlyList<AdaptiveBehavior> Read(string xml)
    {
        var rows = new List<AdaptiveBehavior>();
        foreach (Match item in Items(xml))
        {
            double[] chances = new double[Levels];
            foreach (Match level in LevelAttribute().Matches(item.Value))
            {
                int index = int.Parse(level.Groups["index"].Value, CultureInfo.InvariantCulture);
                if (index < Levels)
                {
                    chances[index] = double.Parse(level.Groups["value"].Value, CultureInfo.InvariantCulture);
                }
            }
            rows.Add(new AdaptiveBehavior(item.Groups["name"].Value, chances));
        }
        return rows;
    }

    /// <summary><paramref name="xml"/> with each behaviour's levels set from <paramref name="rows"/>.</summary>
    public static string Write(string xml, IEnumerable<AdaptiveBehavior> rows)
    {
        var byName = rows.ToDictionary(r => r.Name);
        Match block = Block().Match(xml);
        if (!block.Success)
        {
            throw new InvalidDataException("gamemodesconfig.xml has no <AdaptativeBehavior> table.");
        }
        string edited = ItemElement().Replace(block.Value, item =>
            !byName.TryGetValue(item.Groups["name"].Value, out AdaptiveBehavior? row)
                ? item.Value
                : LevelAttribute().Replace(item.Value, level =>
                {
                    int index = int.Parse(level.Groups["index"].Value, CultureInfo.InvariantCulture);
                    return index < Levels ? $"level{index}=\"{Format(row.Chances[index])}\"" : level.Value;
                }));
        return string.Concat(xml.AsSpan(0, block.Index), edited, xml.AsSpan(block.Index + block.Length));
    }

    private static string Format(double value) => value.ToString("0.0##", CultureInfo.InvariantCulture);

    private static IEnumerable<Match> Items(string xml)
    {
        Match block = Block().Match(xml);
        return block.Success ? ItemElement().Matches(block.Value) : [];
    }

    [GeneratedRegex(@"<AdaptativeBehavior>.*?</AdaptativeBehavior>", RegexOptions.Singleline)]
    private static partial Regex Block();

    [GeneratedRegex(@"<Item\s+behavior=""(?<name>[^""]+)""[^>]*/>")]
    private static partial Regex ItemElement();

    [GeneratedRegex(@"level(?<index>\d+)=""(?<value>[^""]*)""")]
    private static partial Regex LevelAttribute();
}
