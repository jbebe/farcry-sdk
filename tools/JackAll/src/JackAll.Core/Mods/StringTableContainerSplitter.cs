using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;

namespace JackAll.Core.Mods;

/// <summary>
/// A localized string table (`oasisstrings.rml`) as an <see cref="IContainerSplitter"/>: one
/// fragment per string.
/// </summary>
/// <remarks>
/// 11,394 strings live in one 946 KB file, so without this every weapon rename, menu retitle or
/// retranslation is a whole-file override - last-wins and silent, which means two mods renaming
/// different weapons destroy each other's work with no diagnostic.
///
/// The unit is the individual string, but the *file* a mod stages is one
/// <see cref="OasisStringsPatch"/> document holding all of its edits: a layer expands that document
/// into these fragments when it is scanned. So the payload is the ten strings a weapon mod actually
/// changes, conflicts are reported per string, and a retranslation is still one file rather than
/// 11,394. See docs/design/mod-layout-final.md.
///
/// <see cref="IContainerTree.List"/> is deliberately empty. The table has no browsable child set
/// worth 11,394 rows per language; the rows a person wants are the ones some mod overrides, and
/// <c>GameVfs.BuildFragmentRows</c> already synthesizes exactly those from the override index.
/// </remarks>
public sealed class StringTableContainerSplitter : IContainerSplitter
{
    private const string SectionElement = "section";
    private const string StringElement = "string";
    private const string NameAttribute = "name";
    private const string KeyAttribute = "enum";
    private const string ValueAttribute = "value";

    public static StringTableContainerSplitter Instance { get; } = new();

    /// <summary>
    /// The fragment id one string is addressed by - see <see cref="FragmentId"/>. The table keys a
    /// string by <c>(section, enum)</c> and stores no number, so the number is that pair's hash, the
    /// way a MOVE weapon branch collapses its own composite key.
    /// </summary>
    /// <remarks>
    /// The hash is <see cref="FcbClassDefinitions.Crc32Ascii"/>, not <see cref="NameHash"/>: a key is
    /// a literal string rather than a path, and the `Keyboard` section keys the punctuation keys by
    /// the character they type - <c>/</c>, <c>\</c> and <c>;</c> are all separate strings there.
    /// Hashing them as a path folds <c>/</c> onto <c>\</c> and lowercases besides, which collapses
    /// two real entries onto one id. The label has its own separators replaced for the same reason,
    /// so a key that is a slash cannot read as a directory in the row's path.
    /// </remarks>
    public static string IdOf(string section, string key)
        => FragmentId.Of(NumberOf(section, key), $"{section}.{key}".Replace('\\', '_').Replace('/', '_'));

    public static string IdOf(OasisStringEdit edit) => IdOf(edit.Section, edit.Key);

    private static uint NumberOf(string section, string key)
        => FcbClassDefinitions.Crc32Ascii($"{section};{key}");

    public IContainerTree Open(byte[] container) => new Tree(RmlDocument.Deserialize(container));

    public string Canonicalize(string fragmentXml)
        => OasisStringsPatch.FragmentToXml(OasisStringsPatch.FragmentFromXml(fragmentXml));

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        XElement root = RmlDocument.Deserialize(baseBytes);
        foreach ((string id, string xml) in fragmentXmlById)
        {
            OasisStringEdit edit = OasisStringsPatch.FragmentFromXml(xml);
            if (FragmentId.NumberOf(id) != NumberOf(edit.Section, edit.Key))
            {
                throw new InvalidDataException(
                    $"A localization fragment staged as '{id}' describes '{edit.Section};{edit.Key}' "
                    + $"instead. Name it '{IdOf(edit)}' - any label ahead of the number is yours to "
                    + "choose - or fix the string it names.");
            }

            Set(root, edit);
        }

        return RmlDocument.Serialize(root);
    }

    /// <summary>
    /// Writes one edit into the table, in place where the string already exists so an untouched
    /// table re-encodes to the bytes it came from. A string, or a whole section, a mod introduces is
    /// appended at the end of the place that should hold it.
    /// </summary>
    private static void Set(XElement root, OasisStringEdit edit)
    {
        XElement? section = root.Elements(SectionElement)
            .FirstOrDefault(s => (string?)s.Attribute(NameAttribute) == edit.Section);
        if (section is null)
        {
            section = new XElement(SectionElement, new XAttribute(NameAttribute, edit.Section));
            root.Add(section);
        }

        XElement? entry = section.Elements(StringElement)
            .FirstOrDefault(s => (string?)s.Attribute(KeyAttribute) == edit.Key);
        if (entry is null)
        {
            section.Add(new XElement(StringElement,
                new XAttribute(KeyAttribute, edit.Key), new XAttribute(ValueAttribute, edit.Value)));
            return;
        }

        entry.SetAttributeValue(ValueAttribute, edit.Value);
    }

    /// <summary>Every string in the table, keyed the way a fragment id is.</summary>
    public static IEnumerable<OasisStringEdit> Strings(XElement root)
        => root.Elements(SectionElement).SelectMany(section =>
        {
            string name = (string?)section.Attribute(NameAttribute) ?? string.Empty;
            return section.Elements(StringElement)
                .Where(e => (string?)e.Attribute(KeyAttribute) is { Length: > 0 })
                .Select(e => new OasisStringEdit(
                    name, (string)e.Attribute(KeyAttribute)!, (string?)e.Attribute(ValueAttribute) ?? string.Empty));
        });

    private sealed class Tree(XElement root) : IContainerTree
    {
        private readonly Dictionary<uint, OasisStringEdit> _byId = Index(root);

        public string? Extract(string fragmentId)
            => FragmentId.NumberOf(fragmentId) is { } number && _byId.TryGetValue(number, out OasisStringEdit edit)
                ? OasisStringsPatch.FragmentToXml(edit)
                : null;

        /// <summary>Nothing - see the class remarks. The table is addressable but not browsable.</summary>
        public IReadOnlyList<FcbFragmentInfo> List() => [];

        /// <summary>
        /// Declined. A table that lists no fragments cannot be reduced to markers, and nothing asks:
        /// this format's own override unit is a patch document, so it never reaches the generic
        /// fragment path that compares shapes.
        /// </summary>
        public string? Skeleton(Func<string, bool> keep) => null;

        private static Dictionary<uint, OasisStringEdit> Index(XElement root)
        {
            var byId = new Dictionary<uint, OasisStringEdit>();
            foreach (OasisStringEdit edit in Strings(root))
            {
                byId[NumberOf(edit.Section, edit.Key)] = edit;
            }
            return byId;
        }
    }
}
