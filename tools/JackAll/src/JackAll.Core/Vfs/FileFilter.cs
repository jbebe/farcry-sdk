using System.Globalization;
using System.Text.RegularExpressions;

namespace JackAll.Core.Vfs;

/// <summary>
/// A filter box's text, parsed: path words that must all appear, <c>-words</c> that must not, and the
/// <c>ext:xbt</c>, <c>arch:dlc1</c>, <c>hash:1a2b3c4d</c> and <c>spk:reload</c> tokens, freely combined.
/// </summary>
/// <remarks>
/// <c>arch:</c> matches the module name the caller supplies, so a colliding archive's
/// "folder/name" form works too. An unparsable <c>hash:</c> is dropped rather than matched as text,
/// since a mistyped hash is never a meaningful path substring. <c>spk:</c> matches the label the caller
/// supplies, and quoted, <c>spk:"ak47 reload"</c>, every word in the quotes must appear in it.
/// </remarks>
public sealed partial record FileFilter(string[] Includes, string[] Excludes, string? Extension, string? Archive, uint? Hash, string[] Labels)
{
    public bool IsEmpty => Includes.Length == 0 && Excludes.Length == 0 && Extension is null && Archive is null && Hash is null
        && Labels.Length == 0;

    public static FileFilter Parse(string text)
    {
        string? extension = null;
        string? archive = null;
        uint? hash = null;
        var includes = new List<string>();
        var excludes = new List<string>();
        var labels = new List<string>();

        foreach (string token in Tokens().Matches(text).Select(m => m.Value))
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
            else if (token.StartsWith("spk:", StringComparison.OrdinalIgnoreCase))
            {
                labels.AddRange(token[4..].Trim('"').Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
            hash,
            [.. labels]);
    }

    /// <summary>Words match the path; <c>spk:</c> words match the text <paramref name="label"/> gives a
    /// file, so a file without one never matches them.</summary>
    public bool Matches(VfsFile file, Func<VfsFile, string> moduleName, Func<VfsFile, string?>? label = null)
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
        if (Labels.Length > 0)
        {
            string? text = label?.Invoke(file);
            foreach (string word in Labels)
            {
                if (text?.Contains(word, StringComparison.OrdinalIgnoreCase) != true)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static string NormalizeSlashes(string path) => path.Replace('/', '\\');

    /// <summary>Space-separated tokens, a quoted run counting as one (an unclosed quote runs to the end).</summary>
    [GeneratedRegex("""[^\s"]*"[^"]*("|$)\S*|\S+""")]
    private static partial Regex Tokens();
}
