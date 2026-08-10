using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using JackAll.Core.Format.Rml;
using JackAll.Core.Vfs;

namespace JackAll.Core.Xrefs;

/// <summary>
/// Scans an already-decoded RML/XML tree for asset paths, in attribute values and element text
/// alike. Shared by <see cref="RmlReferenceExtractor"/> and by <see cref="FcbReferenceExtractor"/>'s
/// embedded-RML member case.
/// </summary>
public static class RmlReferenceScan
{
    public static void Scan(XElement root, RefKind kind, uint site, ReferenceSink sink)
    {
        foreach (XElement element in root.DescendantsAndSelf())
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                sink.AddPath(attribute.Value, kind, site);
            }

            // Only leaf text: a parent's Value would concatenate every descendant's text into one
            // string, which can't match a path and would just cost allocations on every node.
            if (!element.HasElements)
            {
                sink.AddPath(element.Value, kind, site);
            }
        }
    }
}

/// <summary>References inside a standalone `.rml` (the game's own compiled-XML format).</summary>
public sealed class RmlReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.Type.Extension is "rml";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        if (RmlDocument.TryDeserialize(content, out XElement? root))
        {
            RmlReferenceScan.Scan(root, RefKind.TextPath, sink.Intern("rml"), sink);
        }
    }
}

/// <summary>
/// References inside a plain-text file - `.xml` configs, Domino `.lua` mission graphs, and `.mgb.desc`
/// package manifests.
/// </summary>
/// <remarks>
/// A regex sweep rather than a parse, on purpose: these are three unrelated grammars (XML, Lua, and
/// the `.desc` manifest dialect), and all three express an asset reference the same way - as a
/// literal path string. Parsing each properly would cost three dependencies to find exactly what one
/// pattern finds. The pattern is only as permissive as it can afford to be because
/// <see cref="ReferencePaths.LooksLikeGamePath"/> still has to accept the result.
/// </remarks>
public sealed partial class TextReferenceExtractor : IReferenceExtractor
{
    /// <summary>Path-shaped runs: word characters, separators, dots and dashes, ending in something
    /// extension-like. Deliberately excludes quotes and whitespace, so a match stops at the string
    /// literal's own delimiters rather than swallowing the rest of the line.</summary>
    [GeneratedRegex(@"[A-Za-z0-9_\-./\\]+\.[A-Za-z0-9]{2,8}", RegexOptions.Compiled)]
    private static partial Regex PathLike();

    public bool CanHandle(VfsFile file)
        => file.Type.Extension is "xml" or "lua" or "desc" or "mgb.desc" or "txt" or "ini";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        // Skipped rather than truncated: a text file this big is a generated data dump, and a
        // half-scanned one would report a misleadingly partial reference list.
        if (content.Length > MaxTextBytes)
        {
            return;
        }

        string text = new UTF8Encoding(false).GetString(content);
        uint site = sink.Intern(file.Type.Extension);

        foreach (Match match in PathLike().Matches(text))
        {
            sink.AddPath(match.Value, RefKind.TextPath, site);
        }
    }

    private const int MaxTextBytes = 4 * 1024 * 1024;
}
