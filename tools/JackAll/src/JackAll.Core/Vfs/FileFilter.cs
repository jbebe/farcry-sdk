using System.Globalization;

namespace JackAll.Core.Vfs;

/// <summary>
/// A filter box's text, parsed: path words that must all appear, <c>-words</c> that must not, and the
/// <c>ext:xbt</c>, <c>arch:dlc1</c> and <c>hash:1a2b3c4d</c> tokens, freely combined.
/// </summary>
/// <remarks>
/// <c>arch:</c> matches the module name the caller supplies, so a colliding archive's
/// "folder/name" form works too. An unparsable <c>hash:</c> is dropped rather than matched as text,
/// since a mistyped hash is never a meaningful path substring.
/// </remarks>
public sealed record FileFilter(string[] Includes, string[] Excludes, string? Extension, string? Archive, uint? Hash)
{
    public bool IsEmpty => Includes.Length == 0 && Excludes.Length == 0 && Extension is null && Archive is null && Hash is null;

    public static FileFilter Parse(string text)
    {
        string? extension = null;
        string? archive = null;
        uint? hash = null;
        var includes = new List<string>();
        var excludes = new List<string>();

        foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                extension = token[4..].TrimStart('.');
            }
            else if (token.StartsWith("arch:", StringComparison.OrdinalIgnoreCase))
            {
                archive = token[5..];
            }
            else if (token.StartsWith("hash:", StringComparison.OrdinalIgnoreCase))
            {
                string hex = token[5..];
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    hex = hex[2..];
                }
                if (uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed))
                {
                    hash = parsed;
                }
            }
            else if (token.StartsWith('-') && token.Length > 1)
            {
                excludes.Add(token[1..]);
            }
            else
            {
                includes.Add(token);
            }
        }

        return new FileFilter(
            [.. includes.Select(NormalizeSlashes)],
            [.. excludes.Select(NormalizeSlashes)],
            extension is { Length: > 0 } ? extension : null,
            archive is { Length: > 0 } ? archive : null,
            hash);
    }

    public bool Matches(VfsFile file, Func<VfsFile, string> moduleName)
    {
        if ((Hash is { } hash && file.Hash != hash)
            || (Extension is not null && !string.Equals(file.Type.Extension, Extension, StringComparison.OrdinalIgnoreCase))
            || (Archive is not null && !moduleName(file).Contains(Archive, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        string path = NormalizeSlashes(file.Path);
        foreach (string word in Excludes)
        {
            if (path.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        foreach (string word in Includes)
        {
            if (!path.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }

    private static string NormalizeSlashes(string path) => path.Replace('/', '\\');
}
