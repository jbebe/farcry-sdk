using System.Text;
using System.Xml;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// The control config files as an <see cref="IContainerSplitter"/>: one fragment per named section,
/// a <c>&lt;Category&gt;</c> of <c>defaultusercontrols.xml</c> or an <c>&lt;ActionMap&gt;</c> of an
/// <c>inputactionmap*.xml</c>, merged as a list. Anything at the root without a name stays with the
/// base file. See docs/design/mod-layout-final.md.
/// </summary>
public sealed class InputConfigContainerSplitter : IContainerSplitter
{
    private const string NameAttribute = "name";

    public static InputConfigContainerSplitter Instance { get; } = new();

    /// <summary>Whether this file is the control list or one of the action maps.</summary>
    public static bool IsInputConfig(string path)
    {
        string fileName = Path.GetFileName(path);
        return fileName.Equals("defaultusercontrols.xml", StringComparison.OrdinalIgnoreCase)
            || (fileName.StartsWith("inputactionmap", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A section's id: its name, as a file.</summary>
    public static string IdOf(string sectionName) => FcbFragments.Sanitize(sectionName) + ".xml";

    public IContainerTree Open(byte[] container) => new Tree(Load(container, LoadOptions.None).Root!);

    public string Canonicalize(string fragmentId, string fragmentXml) => Render(XElement.Parse(fragmentXml));

    public (string Merged, bool Conflict) Merge(string fragmentId, string ancestor, string ours, string theirs)
    {
        (XElement merged, bool conflict) = XmlListMerge.Merge(
            ancestor.Length == 0 ? null : XElement.Parse(ancestor), XElement.Parse(ours), XElement.Parse(theirs),
            KeyOf);
        return (Render(merged), conflict);
    }

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        XDocument document = Load(baseBytes, LoadOptions.PreserveWhitespace);
        XElement root = document.Root!;
        Dictionary<string, XElement> byId = Index(root);
        foreach ((string id, string xml) in fragmentXmlById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            XElement replacement = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            string name = (string?)replacement.Attribute(NameAttribute) ?? "";
            if (name.Length == 0 || !FcbFragments.IdComparer.Equals(IdOf(name), id))
            {
                throw new InvalidDataException(
                    $"A fragment staged as '{id}' holds a <{replacement.Name.LocalName}> named '{name}'. Name "
                    + $"the file '{(name.Length == 0 ? id : IdOf(name))}', or fix the name it holds.");
            }

            if (byId.TryGetValue(id, out XElement? existing))
            {
                existing.ReplaceWith(replacement);
            }
            else
            {
                root.Add(replacement);
            }
            byId[id] = replacement;
        }

        return Save(document);
    }

    /// <summary>What a section's child is matched by in a merge: its name, else the action map it
    /// imports, else its whole text.</summary>
    private static string KeyOf(XElement child)
        => child.Name.LocalName.ToLowerInvariant() + "|"
            + ((string?)child.Attribute(NameAttribute)
                ?? (string?)child.Attribute("actionmap")
                ?? child.ToString(SaveOptions.DisableFormatting));

    /// <summary>Every named section of a file under the id a mod stages it at.</summary>
    private static Dictionary<string, XElement> Index(XElement root)
    {
        var byId = new Dictionary<string, XElement>(FcbFragments.IdComparer);
        foreach (XElement section in root.Elements())
        {
            if ((string?)section.Attribute(NameAttribute) is { Length: > 0 } name)
            {
                byId[IdOf(name)] = section;
            }
        }
        return byId;
    }

    /// <summary>The file as its declaration says it is encoded.</summary>
    private static XDocument Load(byte[] bytes, LoadOptions options)
    {
        using var stream = new MemoryStream(bytes);
        return XDocument.Load(stream, options);
    }

    /// <summary>The file in the encoding its declaration names, without a byte order mark, and with
    /// the CRLF line endings the shipped files use.</summary>
    private static byte[] Save(XDocument document)
    {
        Encoding encoding = document.Declaration?.Encoding is { Length: > 0 } name
            ? Encoding.GetEncoding(name)
            : Encoding.UTF8;
        var settings = new XmlWriterSettings
        {
            Encoding = encoding is UTF8Encoding ? new UTF8Encoding(false) : encoding,
            OmitXmlDeclaration = document.Declaration is null,
            NewLineChars = "\r\n",
        };

        using var stream = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }
        return stream.ToArray();
    }

    /// <summary>One section in the shape every staged fragment is written in, tab-indented like the
    /// shipped files.</summary>
    private static string Render(XElement section) => FragmentXml.Render(section, "\t");

    private sealed class Tree(XElement root) : IContainerTree
    {
        private readonly Dictionary<string, XElement> _byId = Index(root);

        public string? Extract(string fragmentId)
            => _byId.TryGetValue(fragmentId, out XElement? section) ? Render(section) : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _byId.Select(kv => new FcbFragmentInfo(kv.Key, Render(kv.Value).Length))];

        /// <summary>The file with every kept section reduced to a marker and the rest dropped, so an
        /// importer can tell a changed section from a change to the file around them.</summary>
        public string? Skeleton(Func<string, bool> keep)
        {
            var clone = new XElement(root.Name, root.Attributes());
            foreach (XElement child in root.Elements())
            {
                if ((string?)child.Attribute(NameAttribute) is not { Length: > 0 } name)
                {
                    clone.Add(new XElement(child));
                }
                else if (keep(IdOf(name)))
                {
                    clone.Add(new XElement(child.Name, new XAttribute(NameAttribute, name)));
                }
            }
            return clone.ToString();
        }
    }
}
