using System.IO.Hashing;

namespace JackAll.Core.Format;

/// <summary>
/// The engine's archive-entry key: CRC32 of the normalized relative path.
/// </summary>
/// <remarks>
/// Normalization is load-bearing and was recovered from the engine, not guessed — see
/// docs/docs/file-formats/archives-fat-dat.md. Get it wrong and every lookup misses silently, which
/// is exactly the failure mode that is hardest to debug later. The hash itself is plain
/// CRC-32/ISO-HDLC (the same one zip/gzip use), so it's just <see cref="Crc32"/>.
/// </remarks>
public static class NameHash
{
    /// <summary>The source extensions the engine swaps for the cooked one before hashing, as
    /// config/resourceconfig.xml lists them.</summary>
    private static readonly Dictionary<string, string> CookedExtensions = new(StringComparer.Ordinal)
    {
        [".glm"] = ".xbg", [".dds"] = ".xbt", [".png"] = ".xbt", [".mac"] = ".mab",
        [".skel.xml"] = ".skeleton", [".fxa"] = ".lfa", [".fxe"] = ".lfe", [".hkr"] = ".hkx",
        [".rta"] = ".rtx", [".frank"] = ".apm", [".mlm"] = ".xbm", [".gsdat"] = ".sdat",
        [".ai.xml"] = ".ai.rml",
    };

    /// <summary>
    /// Lowercase, every run of slashes to one backslash, and one leading backslash dropped - but a
    /// leading <c>\\</c> kept, and a leading forward slash kept as a backslash.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(path.Length);
        int start = 0;
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            sb.Append(@"\\");
            start = 2;
        }
        else if (path[0] == '\\')
        {
            start = 1;
        }

        bool prevSeparator = false;
        foreach (char c in path.AsSpan(start))
        {
            if (c is '/' or '\\')
            {
                if (!prevSeparator)
                {
                    sb.Append('\\');
                }
                prevSeparator = true;
            }
            else
            {
                prevSeparator = false;
                sb.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// <see cref="Normalize"/>, then a source extension swapped for the cooked one - the path the
    /// engine actually looks up. The extension runs from the first dot of the last component.
    /// </summary>
    public static string ResourcePath(string path)
    {
        string normalized = Normalize(path);
        int name = normalized.LastIndexOf('\\') + 1;
        int dot = normalized.IndexOf('.', name);
        return dot > 0 && CookedExtensions.TryGetValue(normalized[dot..], out string? cooked)
            ? normalized[..dot] + cooked
            : normalized;
    }

    /// <summary>CRC32 of <see cref="ResourcePath"/> — the value stored in the .fat index. An empty
    /// path has no key, which the engine writes as 0xFFFFFFFF.</summary>
    public static uint Compute(string path)
    {
        string resource = ResourcePath(path);
        if (resource.Length == 0)
        {
            return uint.MaxValue;
        }

        // Paths are ASCII; the engine hashes the raw bytes of a narrow string.
        Span<byte> bytes = resource.Length <= 256 ? stackalloc byte[resource.Length] : new byte[resource.Length];
        for (int i = 0; i < resource.Length; i++)
        {
            bytes[i] = (byte)resource[i];
        }

        return Crc32.HashToUInt32(bytes);
    }
}
