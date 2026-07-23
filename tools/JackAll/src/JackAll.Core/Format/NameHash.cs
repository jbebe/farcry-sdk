using System.IO.Hashing;

namespace JackAll.Core.Format;

/// <summary>
/// The engine's archive-entry key: CRC32 of the normalized relative path.
/// </summary>
/// <remarks>
/// Normalization is load-bearing and was recovered from the engine, not guessed — see
/// reverse/dunia/archive_loading.md (per-entry lookup). Get it wrong and every lookup misses
/// silently, which is exactly the failure mode that is hardest to debug later. The hash itself is
/// plain CRC-32/ISO-HDLC (the same one zip/gzip use), so it's just <see cref="Crc32"/>.
/// </remarks>
public static class NameHash
{
    /// <summary>
    /// Lowercase, forward slashes to backslashes, collapse repeated separators, drop a leading one.
    /// </summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(path.Length);
        bool prevSeparator = false;
        foreach (char c in path)
        {
            if (c is '/' or '\\')
            {
                if (prevSeparator)
                {
                    continue;
                }
                prevSeparator = true;
                sb.Append('\\');
            }
            else
            {
                prevSeparator = false;
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        if (sb.Length > 0 && sb[0] == '\\')
        {
            sb.Remove(0, 1);
        }
        return sb.ToString();
    }

    /// <summary>CRC32 of the normalized path — the value stored in the .fat index.</summary>
    public static uint Compute(string path)
    {
        string normalized = Normalize(path);

        // Paths are ASCII; the engine hashes the raw bytes of a narrow string.
        Span<byte> bytes = normalized.Length <= 256 ? stackalloc byte[normalized.Length] : new byte[normalized.Length];
        for (int i = 0; i < normalized.Length; i++)
        {
            bytes[i] = (byte)normalized[i];
        }

        return Crc32.HashToUInt32(bytes);
    }
}
