using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using JackAll.Core.Format.Fcb;

namespace JackAll.App.SaveGames;

/// <summary>
/// A flat, class-agnostic hash → (name, wire type) lookup built directly from
/// <c>tools/JackAll/assets/binary_classes.xml</c>, for resolving a save's values/objects against real
/// declared names/types where the byte length actually backs up the declared type - see
/// <see cref="SaveGameXmlRenderer"/>, which is the only consumer.
/// </summary>
/// <remarks>
/// This is deliberately NOT the same lookup <see cref="FcbClassDefinitions.FindMember"/> does. That
/// one is scoped: it only finds a member declared on the object's own *resolved class* (walking its
/// superclass chain), which is exactly right for `entitylibrary.fcb`-shaped files where a member name
/// only means one thing in the context of its declaring class. A save's `PersistenceDB` objects never
/// resolve to a known class in the first place (their type-hashes aren't in `binary_classes.xml` at
/// all — see reverse/dunia/savegame_format.md), so the scoped lookup always fails for them regardless
/// of whether the *name* happens to be catalogued somewhere else in the file.
///
/// Tested directly whether that actually matters: cross-referenced every distinct hash in a real
/// save's exported tree against a flat (name → CRC32, ignoring which class declared it) reading of the
/// whole config file. It does — common short field names (`Id`, `Name`, `State`, `Pos`, `Flags`,
/// `EntityId`, ...) are declared under some entity-library class for an unrelated reason, but hash the
/// same way regardless of context, so the same hash the save uses for conceptually the same idea often
/// really is catalogued, just never reachable via the class-scoped path. See
/// reverse/dunia/savegame_format.md's "binary_classes.xml DOES resolve some savegame content" section
/// for the full list and the caveats (a matched hash means "some declared name hashes the same", not
/// proof that's really what this specific field means here — treat these as strong hints, not
/// certainties, the same way this whole area of investigation has been treated throughout).
/// </remarks>
internal static partial class SaveGameFieldCatalog
{
    private static readonly Lazy<CatalogData> Data = new(Load);

    private sealed record CatalogData(
        IReadOnlyDictionary<uint, (string? Name, FcbMemberType Type)> Members,
        IReadOnlyDictionary<uint, string> Classes);

    [GeneratedRegex(@"<member\s+(?:name=""(?<name>[^""]+)""|hash=""(?<hash>[0-9A-Fa-f]{8})"")[^>]*>(?<type>[A-Za-z0-9]*)</member>")]
    private static partial Regex MemberElement();

    [GeneratedRegex(@"<class\s+name=""(?<name>[^""]+)""")]
    private static partial Regex NamedClassElement();

    /// <summary>A declared member name/type for <paramref name="hash"/>, if <c>binary_classes.xml</c>
    /// knows one - the caller (<see cref="SaveGameXmlRenderer"/>) still has to check the byte length
    /// actually matches <paramref name="type"/> before trusting it; this lookup alone doesn't (a hash
    /// match alone only proves the string coincides, not that this specific field really carries that
    /// type - see remarks above).</summary>
    public static bool TryResolveMember(uint hash, out string? name, out FcbMemberType type)
    {
        if (Data.Value.Members.TryGetValue(hash, out (string? Name, FcbMemberType Type) info))
        {
            (name, type) = info;
            return true;
        }
        (name, type) = (null, FcbMemberType.BinHex);
        return false;
    }

    /// <summary>A declared class name for <paramref name="hash"/>, if <c>binary_classes.xml</c> knows
    /// one - unlike <see cref="TryResolveMember"/> there's no byte-length gate for an object's own type
    /// hash, so a match here is used as-is.</summary>
    public static bool TryResolveClassName(uint hash, out string? name)
        => Data.Value.Classes.TryGetValue(hash, out name);

    private static CatalogData Load()
    {
        var members = new Dictionary<uint, (string? Name, FcbMemberType Type)>();
        var classes = new Dictionary<uint, string>();

        if (!File.Exists(AppConfig.BinaryClassesFile))
        {
            return new CatalogData(members, classes);
        }

        string xml = File.ReadAllText(AppConfig.BinaryClassesFile);

        foreach (Match m in MemberElement().Matches(xml))
        {
            if (!Enum.TryParse(m.Groups["type"].Value, out FcbMemberType type))
            {
                continue;
            }

            if (m.Groups["name"] is { Success: true } nameGroup)
            {
                members.TryAdd(FcbClassDefinitions.Crc32Ascii(nameGroup.Value), (nameGroup.Value, type));
            }
            else
            {
                // Hash-only declaration: binary_classes.xml itself never learned this member's name,
                // but the wire type is still real and worth using - matches FcbXml's own behavior
                // (WriteValueEntry falls back to a `hash="..."` attribute but keeps the real type).
                uint hash = uint.Parse(m.Groups["hash"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                members.TryAdd(hash, (null, type));
            }
        }

        foreach (Match m in NamedClassElement().Matches(xml))
        {
            string name = m.Groups["name"].Value;
            classes.TryAdd(FcbClassDefinitions.Crc32Ascii(name), name);
        }

        return new CatalogData(members, classes);
    }
}
