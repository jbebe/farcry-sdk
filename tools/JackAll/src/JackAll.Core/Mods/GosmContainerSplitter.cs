using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// A pawn state machine, <c>*.gosm.xml</c>, as an <see cref="IContainerSplitter"/>: one fragment per
/// root <c>Group</c>, <c>State</c> or <c>Track</c> by its <c>FullName</c>, merged child by child, so
/// two mods adding to one group both land. Anything at the root without a <c>FullName</c> stays with
/// the base file.
/// </summary>
public sealed class GosmContainerSplitter : IContainerSplitter
{
    public const string Suffix = ".gosm.xml";

    private const string NameAttribute = "FullName";

    /// <summary>The attributes a child is told apart by, in the order they are tried.</summary>
    private static readonly string[] KeyAttributes = [NameAttribute, "Path", "Name"];

    public static GosmContainerSplitter Instance { get; } = new();

    public static bool IsStateMachine(string path) => path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>An element's id: its <c>FullName</c>, as a file.</summary>
    public static string IdOf(string fullName) => FcbFragments.Sanitize(fullName) + ".xml";

    public IContainerTree Open(byte[] container) => new Tree(XmlFileText.Load(container, LoadOptions.None).Root!);

    public string Canonicalize(string fragmentId, string fragmentXml) => Render(XElement.Parse(fragmentXml));

    public (string Merged, IReadOnlyList<string> Conflicts) Merge(string fragmentId, string ancestor, string ours, string theirs)
        => XmlListMerge.MergeTree(ancestor, ours, theirs, KeyOf);

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
            XElement replacement = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            string? held = IdOf(replacement);
            if (held is null || !FcbFragments.IdComparer.Equals(held, id))
            {
                throw new InvalidDataException(
                    $"A fragment staged as '{id}' holds a <{replacement.Name.LocalName}> that belongs at "
                    + $"'{held ?? "no id: it needs a FullName"}'. Name the file after what it holds.");
            }

            if (byId.TryGetValue(id, out XElement? existing))
            {
                if (existing.Name != replacement.Name)
                {
                    throw new InvalidDataException(
                        $"A fragment staged as '{id}' holds a <{replacement.Name.LocalName}>, but that id is the "
                        + $"file's <{existing.Name.LocalName}>.");
                }
                existing.ReplaceWith(replacement);
            }
            else
            {
                root.Add(replacement);
            }
            byId[id] = replacement;
        }

        return XmlFileText.Save(document);
    }

    /// <summary>What a child is matched by in a merge: its element name and its name, or a connection's
    /// target and signal, since one sink connects to a target on several signals.</summary>
    private static string KeyOf(XElement child)
    {
        if (child.Name.LocalName == "Connection")
        {
            return $"Connection|{(string?)child.Attribute("Target")}|{(string?)child.Attribute("Signal")}";
        }
        string? name = KeyAttributes.Select(a => (string?)child.Attribute(a)).FirstOrDefault(v => v is not null);
        return name is null ? child.Name.LocalName : $"{child.Name.LocalName}|{name}";
    }

    /// <summary>The id a root element would be staged at, or null when it is not a fragment.</summary>
    private static string? IdOf(XElement element)
        => element.Parent?.Parent is null && (string?)element.Attribute(NameAttribute) is { Length: > 0 } name
            ? IdOf(name)
            : null;

    /// <summary>Every root element in the file under the id a mod stages it at.</summary>
    private static Dictionary<string, XElement> Index(XElement root)
    {
        var byId = new Dictionary<string, XElement>(FcbFragments.IdComparer);
        foreach (XElement element in root.Elements())
        {
            if (IdOf(element) is { } id)
            {
                byId.TryAdd(id, element);
            }
        }
        return byId;
    }

    private static string Render(XElement element) => FragmentXml.Render(element, "\t");

    private sealed class Tree(XElement root) : IContainerTree
    {
        private readonly Dictionary<string, XElement> _byId = Index(root);

        public string? Extract(string fragmentId)
            => _byId.TryGetValue(fragmentId, out XElement? element) ? Render(element) : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _byId.Select(kv => new FcbFragmentInfo(kv.Key, kv.Value.ToString(SaveOptions.DisableFormatting).Length))];

        /// <summary>The file with every kept element reduced to a marker and the rest dropped.</summary>
        public string? Skeleton(Func<string, bool> keep)
        {
            var clone = new XElement(root.Name, root.Attributes());
            foreach (XElement child in root.Elements())
            {
                if (IdOf(child) is not { } id || _byId[id] != child)
                {
                    clone.Add(new XElement(child));
                }
                else if (keep(id))
                {
                    clone.Add(new XElement(child.Name, new XAttribute("id", id)));
                }
            }
            return clone.ToString();
        }
    }
}
