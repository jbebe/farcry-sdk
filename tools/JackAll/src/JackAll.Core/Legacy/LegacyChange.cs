using System.IO.Hashing;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JackAll.Core.Legacy;

/// <summary>What one change does to the unit it sits in.</summary>
public enum ChangeKind
{
    /// <summary>A value or attribute inside an XML document differs.</summary>
    Field,

    /// <summary>An element the base game's document lacks.</summary>
    Add,

    /// <summary>An element the mod's document drops.</summary>
    Remove,

    /// <summary>A whole file or fragment the base game does not have.</summary>
    New,

    /// <summary>A whole file that differs and cannot be compared any finer.</summary>
    File,

    /// <summary>One hunk of a text file.</summary>
    Text,

    /// <summary>One run of bytes in a game binary.</summary>
    Bytes,

    /// <summary>A file the mod ships outside the archives, beside the game.</summary>
    Loose,
}

/// <summary>
/// One smallest independently pickable difference between a legacy mod and the base game.
/// </summary>
/// <param name="Unit">The file the change lives in: an archive path (<c>worlds/world1/…</c>), or
/// <c>install/…</c> for anything beside the archives. A pick writes whole units.</param>
/// <param name="Address">The unit, then <c>#</c> and an XML path, <c>@L</c> and a base-game line,
/// or <c>@0x</c> and an RVA. Equal to <paramref name="Unit"/> for a whole-unit change.</param>
/// <param name="Whole">The unit can only be picked with every one of its changes.</param>
/// <param name="Hint">What the unit calls itself - an archetype name, a script's header comment.</param>
/// <param name="Context">For a byte run, the base game's bytes either side of it.</param>
/// <param name="ShadowedBy">The library the game reads this archetype from instead, when a later one
/// declares it again - the change edits a copy nothing loads.</param>
public sealed record LegacyChange(
    ChangeKind Kind,
    string Unit,
    string Address,
    string? Old = null,
    string? New = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Whole = false,
    string? Hint = null,
    string? Context = null,
    string? ShadowedBy = null)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    public static void WriteAll(string path, IEnumerable<LegacyChange> changes)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        foreach (LegacyChange change in changes)
        {
            writer.WriteLine(JsonSerializer.Serialize(change, Json));
        }
    }

    public static List<LegacyChange> ReadAll(string path)
        => [.. System.IO.File.ReadLines(path)
            .Where(line => line.Length > 0)
            .Select(line => JsonSerializer.Deserialize<LegacyChange>(line, Json)!)];

    /// <summary>A short stable handle for citing this change in a feature page.</summary>
    [JsonIgnore]
    public string Id => XxHash64.HashToUInt64(Encoding.UTF8.GetBytes($"{Kind}|{Address}")).ToString("x16")[..10];
}
