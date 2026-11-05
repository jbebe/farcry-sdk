using System.Text.RegularExpressions;

namespace JackAll.Tools.SoundLabels;

/// <summary>
/// The generic categories, picked from what names a sound: an archetype, component and field, an
/// animation path, a config element. The first rule whose pattern matches wins.
/// </summary>
public static partial class SoundCategories
{
    public const string Dialogue = "dialogue";
    public const string Other = "other";

    private static readonly (string Category, Regex Pattern)[] Rules =
    [
        ("music", Rule("music")),
        ("ui", Rule(@"onscreen|popup|interface|button|page|checkout|command|menu|phone(ring|message|nomessage)")),
        ("explosion", Rule("explosion")),
        (Dialogue, Rule(@"tapes/tape|broadcast|phonecall|greetingmessage")),
        ("scene", Rule(@"choreographed_scene")),
        ("animal", Rule(@"^animals|\\animals\\")),
        ("vehicle", Rule(@"^vehicle|^(land|sea|air)\.|\\vehicles\\")),
        ("voice", Rule(@"breath|drowning|deathsound|pain|scream|grunt")),
        ("effect", Rule(@"malaria|stimsound|soundmixing|knockout|biofeedback|postfx|wound")),
        ("ambience", Rule(@"soundregions|soundpoint|ambian|ambient|underwater|\bwind|\brain")),
        ("impact", Rule(@"materialimpacts|realtreefx")),
        ("weapon", Rule(@"^weaponproperties|^weapons\.|\\weapons\\|bullet|passby|weapon")),
        ("impact", Rule(@"impact|collision|rolling|sliding|falling|hitground|destruction|landing")),
        ("foley", Rule(@"^pickups|^pile_archetypes|pickup|climb|stance|crouch|stand|slide|\\animations\\")),
        ("object", Rule(@"^oa_|^object_archetypes|^interactive|^gadgets|^beautifiers")),
        ("effect", Rule(@"^player")),
    ];

    /// <summary>The category of a use described by <paramref name="context"/>, or <see cref="Other"/>.</summary>
    public static string Of(string context)
        => Rules.FirstOrDefault(r => r.Pattern.IsMatch(context)).Category ?? Other;

    /// <summary>A category's place in rule order, for breaking a tie between two.</summary>
    public static int Rank(string category)
        => Array.FindIndex(Rules, r => r.Category == category) is >= 0 and var at ? at : Rules.Length;

    /// <summary>A field name as words: <c>sndPickupGrabSound_1st</c> is "pickup grab, 1st person".</summary>
    public static string Field(string name)
    {
        string bare = SoundPrefix().Replace(name, "");
        string person = PersonSuffix().Match(bare) is { Success: true } m ? $", {m.Groups[1].Value} person" : "";
        bare = PersonSuffix().Replace(bare, "");
        string[] words = [.. Words().Matches(bare)
            .Select(w => w.Value.ToLowerInvariant())
            .Where(w => w is not ("sound" or "id"))];
        return string.Join(' ', words) + person;
    }

    /// <summary>A dotted archetype name's last part as words, skipping the variant suffixes; every
    /// player character is "player".</summary>
    public static string Entity(string name)
    {
        string[] parts = name.Split('.');
        if (parts[0].Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            return "player";
        }
        string last = parts.LastOrDefault(p => p is not ("Multi" or "Persistent")) ?? name;
        return last.Replace('_', ' ');
    }

    /// <summary>The subject and action an animation path describes:
    /// <c>…\weapons\primary\m-16\1stge_uppb_reload_+000fw_prm16_i1.mab</c> is ("m-16", "reload, 1st person").</summary>
    public static (string Subject, string Action) Animation(string path)
    {
        string subject = (Path.GetFileName(Path.GetDirectoryName(path)) ?? "").Replace('_', ' ');
        string clip = Path.GetFileNameWithoutExtension(path);
        string[] tokens = clip.Split('_');
        int at = Array.FindIndex(tokens, t => t.StartsWith("1st", StringComparison.Ordinal)
            || t.StartsWith("3rd", StringComparison.Ordinal) || t.StartsWith("mix", StringComparison.Ordinal));
        if (at < 0 || at + 2 >= tokens.Length)
        {
            return (subject, clip);
        }
        string person = tokens[at][..3] switch { "1st" => ", 1st person", "3rd" => ", 3rd person", _ => "" };
        return (subject, tokens[at + 2] + person);
    }

    /// <summary>Joins parts with " | " into the lowercase context the rules read.</summary>
    public static string Context(params string?[] parts)
        => string.Join(" | ", parts.Where(p => !string.IsNullOrEmpty(p))).ToLowerInvariant();

    private static Regex Rule(string pattern) => new(pattern, RegexOptions.Compiled);

    [GeneratedRegex("^(dis)?snd(tp|ml|ch|pt|res)?")]
    private static partial Regex SoundPrefix();

    [GeneratedRegex("_?(1st|3rd)$")]
    private static partial Regex PersonSuffix();

    [GeneratedRegex("[A-Z]{2,}(?=[A-Z][a-z]|$|[^A-Za-z])|[A-Z]?[a-z]+|[A-Z]+|[0-9]+")]
    private static partial Regex Words();
}
