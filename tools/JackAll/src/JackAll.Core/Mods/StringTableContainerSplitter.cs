using System.Text;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;

namespace JackAll.Core.Mods;

/// <summary>
/// A localised string table (`oasisstrings.rml`) as an <see cref="IContainerSplitter"/>: one
/// fragment per <c>&lt;section&gt;</c>.
/// </summary>
/// <remarks>
/// 11,394 strings live in one 946 KB file, so without this every weapon rename, menu retitle or
/// retranslation is a whole-file override - last-wins and silent, which means two mods renaming
/// different weapons destroy each other's work with no diagnostic. A section is 1.7 KB.
///
/// The split stops at the section even though <c>string@enum</c> is a perfect key: per-string would
/// save 1.6 KB of payload and turn a retranslation into 11,394 files. See
/// docs/design/mod-layout-final.md.
/// </remarks>
public sealed class StringTableContainerSplitter : IContainerSplitter
{
    /// <summary>The file the engine loads, and the only name that splits. Its plain-text
    /// <c>oasisstrings.xml</c> twin ships in `common.dat` only and is a stale pre-patch leftover.</summary>
    public const string FileName = "oasisstrings.rml";

    private const string SectionElement = "section";
    private const string NameAttribute = "name";

    /// <summary>Two spaces, matching the <c>oasisstrings.xml</c> twin the game ships beside it.</summary>
    private const string IndentChars = "  ";

    /// <summary>An RML node costs four packed counts, an attribute three, at one byte each while the
    /// values stay small - which they do for everything but the string table itself.</summary>
    private const long NodeBytes = 4;
    private const long AttributeBytes = 3;

    public static StringTableContainerSplitter Instance { get; } = new();

    /// <summary>The fragment id a section is staged under - see <see cref="FragmentId"/>. The table
    /// stores a name rather than a number, so the number is that name's hash.</summary>
    /// <remarks>
    /// Hashing the label the id already carries makes a hand-written <c>Tutorial.xml</c> resolve to
    /// the same section as the <c>Tutorial.&lt;hash&gt;.xml</c> tooling writes, because
    /// <see cref="FragmentId.NumberOf"/> falls back to hashing a non-numeric stem.
    /// </remarks>
    public static string IdOf(string sectionName) => FragmentId.Of(NameHash.Compute(sectionName), sectionName);

    public IContainerTree Open(byte[] container) => new Tree(RmlDocument.Deserialize(container));

    public string Canonicalize(string fragmentXml) => FragmentToXml(FragmentFromXml(fragmentXml));

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        XElement root = RmlDocument.Deserialize(baseBytes);
        var staged = new Dictionary<uint, XElement>();
        foreach ((string id, string xml) in fragmentXmlById)
        {
            XElement section = FragmentFromXml(xml);
            string name = NameOf(section);
            if (FragmentId.NumberOf(id) != NameHash.Compute(name))
            {
                throw new InvalidDataException(
                    $"A string table fragment staged as '{id}' describes section '{name}' instead. "
                    + $"Name it '{IdOf(name)}' - any label ahead of the number is yours to choose - "
                    + "or fix the section it names.");
            }
            staged[NameHash.Compute(name)] = section;
        }

        // An overridden section keeps its place, so a table nothing was staged against re-encodes to
        // the bytes it came from; a new one is appended, ordered so a build is reproducible.
        foreach (XElement section in root.Elements(SectionElement).ToList())
        {
            if (staged.Remove(NameHash.Compute(NameOf(section)), out XElement? replacement))
            {
                section.ReplaceWith(replacement);
            }
        }

        foreach (XElement addition in staged.Values.OrderBy(NameOf, StringComparer.Ordinal))
        {
            root.Add(addition);
        }

        return RmlDocument.Serialize(root);
    }

    private static string FragmentToXml(XElement section) => FragmentXml.Render(Normalize(section), IndentChars);

    private static XElement FragmentFromXml(string xml)
        => Normalize(XDocument.Parse(xml).Root
            ?? throw new InvalidDataException("The string table fragment is empty."));

    /// <summary>
    /// The element with the indentation a fragment is written with dropped again.
    /// </summary>
    /// <remarks>
    /// An RML node's value is its <see cref="XElement.Value"/>, so whitespace surviving a parse would
    /// be written into the string table and the container would no longer re-encode to the bytes it
    /// came from. Attributes are copied rather than named, so an unexpected one cannot be silently
    /// dropped on the way through.
    /// </remarks>
    private static XElement Normalize(XElement source)
    {
        var element = new XElement(source.Name, source.Attributes());
        foreach (XNode node in source.Nodes())
        {
            if (node is XText text && string.IsNullOrWhiteSpace(text.Value))
            {
                continue;
            }
            element.Add(node is XElement child ? Normalize(child) : node);
        }
        return element;
    }

    private static string NameOf(XElement section)
        => section.Name.LocalName == SectionElement
            && section.Attribute(NameAttribute)?.Value is { Length: > 0 } name
                ? name
                : throw new InvalidDataException(
                    $"A string table fragment is <{section.Name.LocalName}>, but a fragment is one "
                    + $"<{SectionElement} {NameAttribute}=\"...\"> of the table.");

    private sealed class Tree(XElement root) : IContainerTree
    {
        private readonly Dictionary<uint, XElement> _byHash = Index(root);

        public string? Extract(string fragmentId)
            => FragmentId.NumberOf(fragmentId) is { } hash && _byHash.TryGetValue(hash, out XElement? section)
                ? FragmentToXml(section)
                : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. root.Elements(SectionElement).Select(s => new FcbFragmentInfo(IdOf(NameOf(s)), SizeOf(s)))];

        private static Dictionary<uint, XElement> Index(XElement root)
        {
            var byHash = new Dictionary<uint, XElement>();
            foreach (XElement section in root.Elements(SectionElement))
            {
                byHash[NameHash.Compute(NameOf(section))] = section;
            }
            return byHash;
        }

        /// <summary>The section's own footprint in the binary - its nodes and attributes, plus the
        /// NUL-terminated UTF-8 its values add to the string table. Rendering each section's XML just
        /// to measure it would build a megabyte of text nobody reads.</summary>
        private static long SizeOf(XElement section)
        {
            long size = 0;
            foreach (XElement element in section.DescendantsAndSelf())
            {
                size += NodeBytes;
                foreach (XAttribute attribute in element.Attributes())
                {
                    size += AttributeBytes + Encoding.UTF8.GetByteCount(attribute.Value) + 1;
                }
            }
            return size;
        }
    }
}
