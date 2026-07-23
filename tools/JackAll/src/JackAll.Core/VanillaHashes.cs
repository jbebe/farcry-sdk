using System.IO.Hashing;

namespace JackAll.Core;

/// <summary>
/// Known-good XxHash3 digests of a clean, Steam-patched-to-1.03 Far Cry 2 install's .dat/.fat
/// archives (see assets/vanilla_hashes.ini) — the reference <see cref="GameInstall"/>'s files are
/// checked against before this tool trusts them as a base to build from or revert to.
/// </summary>
/// <remarks>
/// XxHash3 rather than a cryptographic hash: nothing here defends against a malicious actor
/// deliberately forging a match, only against corruption, a wrong game version, or a patch.dat
/// someone else already modded — a non-cryptographic hash detects all of those just as well, and
/// at several times the throughput on files that run into the hundreds of MB
/// (<c>System.IO.Hashing</c> is already a <c>JackAll.Core</c> dependency, so this costs nothing new).
///
/// Plain "path = hex" lines rather than a full ini-parser dependency: <c>JackAll.App</c> already
/// has one for the user's own <c>config.ini</c>, but this file is bundled, read-only reference data
/// with a much simpler shape, and pulling the dependency into <c>JackAll.Core</c> just for this
/// would be pure overhead.
/// </remarks>
public sealed class VanillaHashes
{
    private readonly Dictionary<string, string> _hashes;

    private VanillaHashes(Dictionary<string, string> hashes) => _hashes = hashes;

    /// <summary>Missing or unreadable yields an empty instance — every mismatch check below then
    /// simply finds nothing to compare against, rather than failing the load it's guarding.</summary>
    public static VanillaHashes Load(string path)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(path))
        {
            foreach (string rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] is ';' or '#')
                {
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    continue; // a section header like [hashes], or anything else not a "path = hash" line
                }

                string relativePath = line[..eq].Trim();
                string hash = line[(eq + 1)..].Trim();
                if (relativePath.Length > 0 && hash.Length > 0)
                {
                    hashes[relativePath] = hash;
                }
            }
        }

        return new VanillaHashes(hashes);
    }

    /// <summary>
    /// Checks each of <paramref name="relativePaths"/> (relative to <paramref name="dataDir"/>)
    /// against its known-good hash. Returns the ones that are missing or don't match — empty when
    /// everything checks out, including when this instance has no reference hash for a given path at
    /// all (an archive we don't ship a hash for is not this method's call to flag).
    /// </summary>
    public IReadOnlyList<string> FindMismatches(string dataDir, IEnumerable<string> relativePaths)
    {
        List<string> mismatches = [];
        foreach (string relative in relativePaths)
        {
            if (!_hashes.TryGetValue(relative, out string? expected))
            {
                continue;
            }

            string full = Path.Combine(dataDir, relative);
            if (!File.Exists(full) || !string.Equals(ComputeHash(full), expected, StringComparison.OrdinalIgnoreCase))
            {
                mismatches.Add(relative);
            }
        }
        return mismatches;
    }

    public static string ComputeHash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var hasher = new XxHash3();
        hasher.Append(stream);
        return Convert.ToHexStringLower(hasher.GetCurrentHash());
    }
}
