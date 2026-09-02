using System.Globalization;

namespace JackAll.Tools.Reach;

/// <summary>What the shipped verdict list says about one file.</summary>
public readonly record struct ReachListEntry(ReachVerdict Verdict, bool IsDecoy, string Reason);

/// <summary>
/// The checked-in verdict list (<c>assets/fc2.unused.tsv</c>), looked up by file hash.
/// </summary>
/// <remarks>
/// The list holds only the <c>unused</c> and <c>unknown</c> rows, so a hash that isn't in it is
/// reachable - which is why every query here is "what does the list say about this file", never
/// "is this file used". A missing or unreadable list is not fatal: it loads as
/// <see cref="Empty"/>, and every caller then behaves as it did before the list existed.
/// </remarks>
public sealed class ReachList
{
    private readonly Dictionary<uint, ReachListEntry> _byHash;

    public static ReachList Empty { get; } = new([]);

    private ReachList(Dictionary<uint, ReachListEntry> byHash) => _byHash = byHash;

    public int Count => _byHash.Count;

    public static ReachList Load(string path)
    {
        try
        {
            return File.Exists(path) ? Parse(File.ReadLines(path)) : Empty;
        }
        catch (IOException)
        {
            return Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return Empty;
        }
    }

    public static ReachList Parse(IEnumerable<string> lines)
    {
        var byHash = new Dictionary<uint, ReachListEntry>();
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            if (f.Length < 7
                || !uint.TryParse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash)
                || ParseVerdict(f[2]) is not { } verdict)
            {
                continue;
            }

            byHash[hash] = new ReachListEntry(
                verdict, f[3].Contains("DECOY", StringComparison.Ordinal), f[6]);
        }
        return new ReachList(byHash);
    }

    public bool TryGet(uint hash, out ReachListEntry entry) => _byHash.TryGetValue(hash, out entry);

    /// <summary>Whether the list positively says no engine code path reaches this file. False for
    /// an <c>unknown</c> row, which is exactly the case where the analysis declined to decide.</summary>
    public bool IsUnused(uint hash)
        => _byHash.TryGetValue(hash, out ReachListEntry entry) && entry.Verdict == ReachVerdict.Unused;

    private static ReachVerdict? ParseVerdict(string text) => text switch
    {
        "used" => ReachVerdict.Used,
        "used-sp-only" => ReachVerdict.UsedSpOnly,
        "used-mp-only" => ReachVerdict.UsedMpOnly,
        "unused" => ReachVerdict.Unused,
        "unknown" => ReachVerdict.Unknown,
        _ => null,
    };
}
