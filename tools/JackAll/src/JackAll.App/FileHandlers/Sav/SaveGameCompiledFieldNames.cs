using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using JackAll.Tools.Sav;

namespace JackAll.App.FileHandlers.Sav;

/// <summary>
/// Hash to name for a save's <c>PersistenceDB</c>, from <c>assets/savegame_field_names.tsv</c>: every
/// string in the two binaries whose CRC32 appears in a real save. Name-only - a string match is not a
/// declared type. See docs/docs/file-formats/savegame.md.
/// </summary>
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
