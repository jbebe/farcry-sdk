using System.Globalization;
using JackAll.Core.Format;

namespace JackAll.Core.Naming;

/// <summary>
/// Resolves an archive entry's CRC32 back to a filename.
/// </summary>
/// <remarks>
/// The dictionary is community-reconstructed and incomplete — the archives store only hashes, so
/// any name we can't resolve is a file we can still read and edit but can't name (see
/// <see cref="JackAll.Core.Naming.FileTypeSniffer"/>, which gives those an extension from their header).
///
/// The list is deliberately FC2-only. CRC32 over a multi-game path list collides in practice — the
/// FCBConverter authors hit exactly this, and it silently mis-identifies files. First one wins here
/// too, matching what the engine's own index would do.
///
/// The on-disk file is pre-hashed (<c>HHHHHHHH\tname</c>, one entry per line) rather than a bare path
/// list: computing <see cref="NameHash"/> for every one of its ~180,000 entries on every app launch
/// used to cost several seconds (see perf.txt). Hashing is now a one-time, explicit step — the
/// <c>jackall system hash archiveitems</c> CLI command — instead of paid here on every load.
/// </remarks>
public sealed class NameDatabase
{
    private readonly Dictionary<uint, string> _byHash;

    public int Count => _byHash.Count;

    private NameDatabase(Dictionary<uint, string> byHash)
    {
        _byHash = byHash;
    }

    public static NameDatabase Load(string namesFilePath)
    {
        if (!File.Exists(namesFilePath))
        {
            // Not fatal: without names every file shows up under its hash and stays fully usable.
            return new NameDatabase([]);
        }
        return LoadFrom(File.ReadLines(namesFilePath));
    }

    /// <summary>Parses the pre-hashed <c>HHHHHHHH\tname</c> format the file ships in. Lines that
    /// aren't a valid "hash, tab, name" triple (blank, a comment, or malformed) are skipped rather
    /// than failing the whole load.</summary>
    public static NameDatabase LoadFrom(IEnumerable<string> lines)
    {
        var byHash = new Dictionary<uint, string>();

        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            int tab = line.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            if (!uint.TryParse(line[..tab], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hash))
            {
                continue;
            }

            string name = line[(tab + 1)..];
            if (name.Length > 0)
            {
                byHash.TryAdd(hash, name);
            }
        }

        return new NameDatabase(byHash);
    }

    public bool TryResolve(uint hash, out string path) => _byHash.TryGetValue(hash, out path!);
}
