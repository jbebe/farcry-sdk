using System.Text;

namespace JackAll.Core.Naming;

/// <summary>
/// Turns a path recovered from the archives into one that's safe to write under an output folder the
/// user picked.
/// </summary>
/// <remarks>
/// Archive paths don't come from a filesystem — they come from the community hash list (see
/// <see cref="NameDatabase"/>), which is a text file of names nobody has validated. Nothing there
/// guarantees the absence of a <c>..</c> segment or a character Windows won't accept in a filename,
/// and every caller here is writing into a directory the user chose, where escaping it would be a
/// real mistake rather than a cosmetic one. Shared by the CLI's <c>archive extract</c> and the App's
/// folder export so both land the same bytes in the same place.
/// </remarks>
public static class OutputPath
{
    /// <summary>
    /// <paramref name="name"/> as a relative path under an output folder: separators normalized to
    /// this platform's, empty/<c>.</c>/<c>..</c> segments dropped, and characters no filename may
    /// carry replaced with <c>_</c>. Falls back to the whole name flattened into one segment when
    /// there'd otherwise be nothing left.
    /// </summary>
    public static string Relative(string name)
    {
        string[] segments = name.Split('\\', '/');
        var kept = new List<string>(segments.Length);
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                continue;
            }
            kept.Add(Sanitize(segment));
        }
        return kept.Count == 0 ? Sanitize(name.Replace('\\', '_').Replace('/', '_')) : Path.Combine([.. kept]);
    }

    private static string Sanitize(string segment)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(segment.Length);
        foreach (char c in segment)
        {
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }
        return sb.ToString();
    }
}
