using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace JackAll.App.SaveGames;

/// <summary>
/// A large (~960-entry) hash -&gt; name table for a save's <c>PersistenceDB</c> tree, loaded from
/// <c>assets/savegame_field_names.tsv</c>. Complements <see cref="SaveGamePersistenceTags"/> (the small,
/// hand-curated set of structural tags recovered by decompiling <c>CPersistenceDB::SaveDB</c> and its
/// two record classes' <c>RegisterProperties</c>) and <see cref="SaveGameFieldCatalog"/> (which resolves
/// against <c>binary_classes.xml</c>'s own, unrelated data-file class vocabulary).
/// </summary>
/// <remarks>
/// Recovered a different way than either of those: rather than decompiling one <c>RegisterProperties</c>
/// function at a time (there are 300+ in <c>FarCry2_server</c>, all identically named and
/// un-namespaced in a plain function search - see reverse/dunia/savegame_format.md), every field name
/// passed to <c>CNomadObjectDescriptor::PushBackMember</c> across every entity/component class exists as
/// a plain ASCII string literal somewhere in the binary's rodata or symbol table regardless of which
/// function it belongs to. So instead: a dictionary attack, not a per-class trace - pulled every string
/// constant out of both <c>tools/FarCry2_Dedicated_Server_Linux/bin/FarCry2_server</c> (partly via the
/// GhidraMCP bridge's <c>list_strings</c>, then exhaustively via a raw byte-level scan of the file
/// directly) and the PC client's <c>Dunia.dll</c> (same raw scan - a second independent source of the
/// same literal strings, since both binaries compile from the same shared engine source even though only
/// the Linux server's own *functions* are named/demangled), CRC32-hashed each candidate the same way the
/// engine does, and kept only the ones whose hash exactly matches a hash actually present in a real
/// save's exported <c>PersistenceDB</c> tree (<c>tmp/savegame.fcb.xml</c>, 1,046 distinct hashes).
/// Recovered 964 unambiguous names this way (no two candidate strings hashed to the same in-use hash,
/// and any hash also resolvable by <see cref="SaveGameFieldCatalog"/> was excluded to avoid a duplicate
/// annotation), on top of the 50 <see cref="SaveGameFieldCatalog"/> already knew and the 9
/// <see cref="SaveGamePersistenceTags"/> already knew - **97.8% of every distinct hash in this one real
/// save now resolves to a real name**. See reverse/dunia/savegame_format.md for the full methodology,
/// caveats, and the remaining 23 unresolved hashes.
///
/// Name-only, like <see cref="SaveGamePersistenceTags"/> - a hash matching some string found anywhere in
/// the binary is not proof that string is really this field's declaring identifier (two unrelated things
/// could theoretically collide, though CRC32 makes that vanishingly unlikely at this sample size), so no
/// wire type is asserted. Kept out of <see cref="JackAll.Core.Format.Fcb.FcbClassDefinitions"/>/
/// <see cref="JackAll.Core.Format.Fcb.FcbXml"/> for the same reason every other savegame-specific pass in
/// this folder is: unverified enough to belong nowhere near the round-trip-critical machinery the Files
/// tab's real mod-editing depends on - consulted by <see cref="SaveGameXmlRenderer"/> while it builds the
/// tree, not applied as a text patch afterward.
/// </remarks>
internal static partial class SaveGameCompiledFieldNames
{
    private static readonly Lazy<IReadOnlyDictionary<uint, string>> Data = new(Load);

    public static IReadOnlyDictionary<uint, string> ByHash => Data.Value;

    [GeneratedRegex(@"^([0-9A-Fa-f]{8})\t(.+)$")]
    private static partial Regex TsvRow();

    private static IReadOnlyDictionary<uint, string> Load()
    {
        var byHash = new Dictionary<uint, string>();

        if (!File.Exists(AppConfig.SaveGameFieldNamesFile))
        {
            return byHash;
        }

        foreach (string line in File.ReadLines(AppConfig.SaveGameFieldNamesFile))
        {
            Match m = TsvRow().Match(line);
            if (!m.Success)
            {
                continue; // comment/header line
            }

            uint hash = uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byHash.TryAdd(hash, m.Groups[2].Value);
        }

        return byHash;
    }
}
