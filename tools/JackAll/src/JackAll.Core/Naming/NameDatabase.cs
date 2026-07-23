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

    public static NameDatabase LoadFrom(IEnumerable<string> paths)
    {
        var byHash = new Dictionary<uint, string>();

        foreach (string raw in paths)
        {
            string path = raw.Trim();
            if (path.Length == 0 || path[0] is ';' or '#')
            {
                continue;
            }

            string normalized = NameHash.Normalize(path);
            uint hash = NameHash.Compute(normalized);

            byHash.TryAdd(hash, normalized);
        }

        return new NameDatabase(byHash);
    }

    public bool TryResolve(uint hash, out string path) => _byHash.TryGetValue(hash, out path!);
}
