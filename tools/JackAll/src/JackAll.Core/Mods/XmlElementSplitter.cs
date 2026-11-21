using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// A plain XML file as an <see cref="IContainerSplitter"/>: one fragment per element <see cref="IdOf"/>
/// names, merged child by child. Anything else stays with the base file.
/// </summary>
public abstract class XmlElementSplitter : IContainerSplitter
{
    /// <summary>The elements that may be fragments, in file order.</summary>
    protected virtual IEnumerable<XElement> Candidates(XElement root) => root.Elements();

    /// <summary>The id an element is staged at, or null when it is not a fragment.</summary>
    protected abstract string? IdOf(XElement element);

    /// <summary>What a child is matched by in a merge.</summary>
    protected abstract string KeyOf(XElement child);

    /// <summary>What a fragment needs to have an id, for the refusal of one that has none.</summary>
    protected abstract string IdHint { get; }

    /// <summary>Whether a merge decides this child whole rather than attribute by attribute.</summary>
    protected virtual bool IsWhole(XElement child) => false;

    /// <summary>Puts a fragment the file does not hold yet in its place.</summary>
    protected virtual void AddNew(XElement root, XElement fragment) => root.Add(fragment);

    /// <summary>A name as a fragment id: sanitized, as a file.</summary>
    protected static string FileIdOf(string name) => FcbFragments.Sanitize(name) + ".xml";

    /// <summary>The id of a root element named by this attribute, or null for any other element.</summary>
    protected static string? RootIdOf(XElement element, string attribute)
        => element.Parent?.Parent is null && (string?)element.Attribute(attribute) is { Length: > 0 } name
            ? FileIdOf(name)
            : null;

    /// <summary>A child's element name and the first of these attributes it has.</summary>
    protected static string NamedKey(XElement child, IEnumerable<string> attributes)
        => attributes.Select(a => (string?)child.Attribute(a)).FirstOrDefault(v => v is not null) is { } name
            ? $"{child.Name.LocalName}|{name}"
            : child.Name.LocalName;

    public IContainerTree Open(byte[] container) => new Tree(this, XmlFileText.Load(container, LoadOptions.None).Root!);

    public string Canonicalize(string fragmentId, string fragmentXml) => Render(XElement.Parse(fragmentXml));

    public (string Merged, IReadOnlyList<string> Conflicts) Merge(string fragmentId, string ancestor, string ours, string theirs)
        => XmlListMerge.MergeTree(ancestor, ours, theirs, KeyOf, IsWhole);

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        XDocument document = XmlFileText.Load(baseBytes, LoadOptions.PreserveWhitespace);
        XElement root = document.Root!;
        Dictionary<string, XElement> byId = Index(root);
        foreach ((string id, string xml) in fragmentXmlById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            XElement fragment = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            string? held = IdOf(fragment);
            if (held is null || !FcbFragments.IdComparer.Equals(held, id))
            {
                throw new InvalidDataException(
                    $"A fragment staged as '{id}' holds a <{fragment.Name.LocalName}> that belongs at "
                    + $"'{held ?? "no id: " + IdHint}'. Name the file after what it holds.");
            }

            if (!byId.TryGetValue(id, out XElement? existing))
            {
                AddNew(root, fragment);
            }
            else if (existing.Name == fragment.Name)
            {
                existing.ReplaceWith(fragment);
            }
            else
            {
                throw new InvalidDataException(
                    $"A fragment staged as '{id}' holds a <{fragment.Name.LocalName}>, but that id is the file's "
                    + $"<{existing.Name.LocalName}>, which only the whole file can change.");
            }
            byId[id] = fragment;
        }

        return XmlFileText.Save(document);
    }

    /// <summary>Every fragment in the file under its id; of two whose ids match but for case, the first.</summary>
    private Dictionary<string, XElement> Index(XElement root)
    {
        var byId = new Dictionary<string, XElement>(FcbFragments.IdComparer);
        foreach (XElement element in Candidates(root))
        {
            if (IdOf(element) is { } id)
            {
                byId.TryAdd(id, element);
            }
        }
        return byId;
    }

    private static string Render(XElement element) => FragmentXml.Render(element, "\t");

    private sealed class Tree : IContainerTree
    {
        private readonly XElement _root;
        private readonly Dictionary<string, XElement> _byId;
        private readonly Dictionary<XElement, string> _idOf;

        public Tree(XmlElementSplitter splitter, XElement root)
        {
            _root = root;
            _byId = splitter.Index(root);
            _idOf = _byId.ToDictionary(kv => kv.Value, kv => kv.Key);
        }

        public string? Extract(string fragmentId)
            => _byId.TryGetValue(fragmentId, out XElement? element) ? Render(element) : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _byId.Select(kv => new FcbFragmentInfo(kv.Key, kv.Value.ToString(SaveOptions.DisableFormatting).Length))];

        public string? Skeleton(Func<string, bool> keep) => Reduce(_root, keep).ToString();

        /// <summary>The element with every kept fragment as a marker, every other one dropped, and the rest whole.</summary>
        private XElement Reduce(XElement element, Func<string, bool> keep)
        {
            var reduced = new XElement(element.Name, element.Attributes());
            foreach (XElement child in element.Elements())
            {
                if (!_idOf.TryGetValue(child, out string? id))
                {
                    reduced.Add(child.Elements().Any(_idOf.ContainsKey) ? Reduce(child, keep) : new XElement(child));
                }
                else if (keep(id))
                {
                    reduced.Add(new XElement(child.Name, new XAttribute("id", id)));
                }
            }
            return reduced;
        }
    }
}
