using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Format;

/// <summary>
/// The engine's archive-entry key: CRC32 of the normalized relative path.
/// </summary>
/// <remarks>
/// Normalization is load-bearing and was recovered from the engine, not guessed — see
/// docs/docs/file-formats/archives-fat-dat.md. Get it wrong and every lookup misses silently, which
/// is exactly the failure mode that is hardest to debug later. When the engine resolves a reference
/// it also swaps a source extension such as <c>.dds</c> for the cooked one; a file is keyed by its own
/// name, so a caller resolving a reference makes that swap itself.
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
                if (!prevSeparator && sb.Length > 0)
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

    /// <summary>CRC32 of the normalized path — the value stored in the .fat index.</summary>
    public static uint Compute(string path) => FcbClassDefinitions.Crc32Ascii(Normalize(path));
}
