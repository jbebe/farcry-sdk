using System.Text;
using System.Xml;
using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// A UI dependency list (<c>ui\localized\&lt;aspect&gt;\&lt;lang&gt;\ui\_depload.xml</c>) as an
/// <see cref="IContainerSplitter"/>: one fragment per top-level resource, a package's
/// <c>.mgb.desc</c> or a sound bank, holding everything the game preloads with it.
/// </summary>
/// <remarks>
/// The game reads this list, not the <c>.desc</c> files, to know what a UI package needs, and opens a
/// UI file only once it is loaded, so a new texture or font for a package is listed here. A fragment
/// is staged as <c>&lt;file name&gt;.&lt;crc_ID&gt;.xml</c>, such as
/// <c>fonts.mgb.desc.2312048719.xml</c>. See docs/docs/file-formats/depload.md.
/// </remarks>
public sealed class UiDepLoadContainerSplitter : IContainerSplitter
{
    public const string FileName = "_depload.xml";
    private const string IdAttribute = "ID";
    private const string CrcAttribute = "crc_ID";

    public static UiDepLoadContainerSplitter Instance { get; } = new();

    /// <summary>Whether this is a UI list. A world's <c>&lt;world&gt;_depload.xml</c> is the unread
    /// twin of its binary list, so it is not one.</summary>
    public static bool IsUiDepLoad(string path) => Path.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>A resource's id: its file name as the label, its <c>crc_ID</c> as the number.</summary>
    public static string IdOf(string resourcePath) => FragmentId.Of(NameHash.Compute(resourcePath), resourcePath);

    public IContainerTree Open(byte[] container) => new Tree(Load(container).Root!);

    public string Canonicalize(string fragmentId, string fragmentXml) => Render(XElement.Parse(fragmentXml));

    /// <summary>A resource's dependencies are a list matched by hash, so two mods adding to one
    /// package both keep theirs.</summary>
    public (string Merged, IReadOnlyList<string> Conflicts) Merge(string fragmentId, string ancestor, string ours, string theirs)
        => XmlListMerge.MergeTree(ancestor, ours, theirs, KeyOf);

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        XDocument document = Load(baseBytes);
        XElement root = document.Root!;
        Dictionary<string, XElement> byId = Index(root);
        foreach ((string id, string xml) in fragmentXmlById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            XElement resource = XElement.Parse(xml);
            string path = (string?)resource.Attribute(IdAttribute) ?? "";
            if (path.Length == 0 || FragmentId.NumberOf(id) != NameHash.Compute(path))
            {
                throw new InvalidDataException(
                    $"A fragment staged as '{id}' holds the resource '{path}'. Name the file "
                    + $"'{(path.Length == 0 ? id : IdOf(path))}', or fix the resource it holds.");
            }

            if (byId.TryGetValue(id, out XElement? existing))
            {
                existing.ReplaceWith(resource);
            }
            else
            {
                root.Add(resource);
            }
            byId[id] = resource;
        }
        return Save(document);
    }

    private static string KeyOf(XElement child)
        => $"{child.Name.LocalName}|{(string?)child.Attribute(CrcAttribute) ?? (string?)child.Attribute(IdAttribute)}";

    private static Dictionary<string, XElement> Index(XElement root)
    {
        var byId = new Dictionary<string, XElement>(FcbFragments.IdComparer);
        foreach (XElement resource in root.Elements())
        {
            if ((string?)resource.Attribute(IdAttribute) is { Length: > 0 } path)
            {
                byId[IdOf(path)] = resource;
            }
        }
        return byId;
    }

    private static XDocument Load(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return XDocument.Load(stream);
    }

    /// <summary>The file as the game ships it: tab-indented, LF line endings, no declaration, and a
    /// line end after the root.</summary>
    private static byte[] Save(XDocument document)
    {
        XmlWriterSettings settings = FragmentXml.Settings("\t");
        settings.NewLineChars = "\n";
        settings.Encoding = new UTF8Encoding(false);
        using var stream = new MemoryStream();
        using (XmlWriter writer = XmlWriter.Create(stream, settings))
        {
            document.Save(writer);
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static string Render(XElement resource) => FragmentXml.Render(resource, "\t");

    private sealed class Tree(XElement root) : IContainerTree
    {
        private readonly Dictionary<string, XElement> _byId = Index(root);

        public string? Extract(string fragmentId)
            => _byId.TryGetValue(fragmentId, out XElement? resource) ? Render(resource) : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _byId.Select(kv => new FcbFragmentInfo(kv.Key, Render(kv.Value).Length))];

        /// <summary>The list with every kept resource reduced to its path and the rest dropped.</summary>
        public string? Skeleton(Func<string, bool> keep)
            => new XElement(root.Name, root.Elements()
                .Select(r => (string?)r.Attribute(IdAttribute) ?? "")
                .Where(path => path.Length == 0 || keep(IdOf(path)))
                .Select(path => new XElement("Resource", new XAttribute(IdAttribute, path)))).ToString();
    }
}
