using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// <c>gamemodesconfig.xml</c> as an <see cref="IContainerSplitter"/>: one fragment per property block
/// of <c>&lt;GameModeProperties&gt;</c> (<c>DefaultMuzzleFlashManager.xml</c>) and one per
/// <c>&lt;GameMode&gt;</c> by its <c>Desc</c> name (<c>GameMode.Single.xml</c>), merged attribute by
/// attribute. Anything else at the root stays with the base file.
/// </summary>
public sealed class GameModesConfigContainerSplitter : IContainerSplitter
{
    public const string FileName = "gamemodesconfig.xml";

    private const string PropertiesElement = "GameModeProperties";
    private const string ModeElement = "GameMode";
    private const string ModePrefix = ModeElement + ".";

    /// <summary>The attributes a repeated child is told apart by, in the order they are tried.</summary>
    private static readonly string[] KeyAttributes = ["name", "Name", "ClassName", "id"];

    public static GameModesConfigContainerSplitter Instance { get; } = new();

    public static bool IsGameModesConfig(string path)
        => Path.GetFileName(path).Equals(FileName, StringComparison.OrdinalIgnoreCase);

    /// <summary>A property block's id: its element name, as a file.</summary>
    public static string BlockIdOf(string elementName) => FcbFragments.Sanitize(elementName) + ".xml";

    /// <summary>A game mode's id: its <c>Desc</c> name behind <c>GameMode.</c>.</summary>
    public static string ModeIdOf(string modeName) => ModePrefix + FcbFragments.Sanitize(modeName) + ".xml";

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
        XElement properties = root.Element(PropertiesElement)
            ?? throw new InvalidDataException($"{FileName} has no <{PropertiesElement}> to splice blocks into.");
        Dictionary<string, XElement> byId = Index(root);
        foreach ((string id, string xml) in fragmentXmlById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            XElement replacement = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            string? held = IdOf(replacement);
            if (held is null || !FcbFragments.IdComparer.Equals(held, id))
            {
                throw new InvalidDataException(
                    $"A fragment staged as '{id}' holds a <{replacement.Name.LocalName}> that belongs at "
                    + $"'{held ?? "no id: a game mode needs <Desc Name=\"...\">"}'. Name the file after what it holds.");
            }

            if (byId.TryGetValue(id, out XElement? existing))
            {
                if (existing.Name != replacement.Name)
                {
                    throw new InvalidDataException(
                        $"A fragment staged as '{id}' holds a <{replacement.Name.LocalName}>, but that id is the "
                        + $"file's <{existing.Name.LocalName}>: a block named like another but for case can only "
                        + "be changed by shipping the whole file.");
                }
                existing.ReplaceWith(replacement);
            }
            else if (replacement.Name.LocalName == ModeElement)
            {
                (root.Elements(ModeElement).LastOrDefault() ?? properties).AddAfterSelf(replacement);
            }
            else
            {
                properties.Add(replacement);
            }
            byId[id] = replacement;
        }

        return XmlFileText.Save(document);
    }

    /// <summary>What a child is matched by in a merge: its element name and, where it has one, its name.</summary>
    private static string KeyOf(XElement child)
    {
        string? name = KeyAttributes.Select(a => (string?)child.Attribute(a)).FirstOrDefault(v => v is not null);
        return name is null ? child.Name.LocalName : $"{child.Name.LocalName}|{name}";
    }

    /// <summary>The id an element would be staged at, or null when it is not a fragment.</summary>
    private static string? IdOf(XElement element)
    {
        if (element.Name.LocalName == ModeElement)
        {
            return (string?)element.Element("Desc")?.Attribute("Name") is { Length: > 0 } mode ? ModeIdOf(mode) : null;
        }
        return element.Parent?.Name.LocalName is null or PropertiesElement ? BlockIdOf(element.Name.LocalName) : null;
    }

    /// <summary>Every block and game mode in the file under the id a mod stages it at. Ids ignore case,
    /// so of two blocks named alike but for case (<c>EditorUiService</c>, <c>EditorUIService</c>) the
    /// first is the fragment and the second stays with the base file.</summary>
    private static Dictionary<string, XElement> Index(XElement root)
    {
        var byId = new Dictionary<string, XElement>(FcbFragments.IdComparer);
        foreach (XElement element in root.Elements(ModeElement).Concat(root.Element(PropertiesElement)?.Elements() ?? []))
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

        /// <summary>The file with every kept block and mode reduced to a marker and the rest dropped.</summary>
        public string? Skeleton(Func<string, bool> keep)
        {
            var clone = new XElement(root.Name, root.Attributes());
            foreach (XElement child in root.Elements())
            {
                if (child.Name.LocalName == PropertiesElement)
                {
                    var properties = new XElement(child.Name, child.Attributes());
                    foreach (XElement block in child.Elements())
                    {
                        Reduce(properties, block, keep);
                    }
                    clone.Add(properties);
                }
                else
                {
                    Reduce(clone, child, keep);
                }
            }
            return clone.ToString();
        }

        /// <summary>A fragment as its marker when kept, anything else whole.</summary>
        private void Reduce(XElement into, XElement element, Func<string, bool> keep)
        {
            if (IdOf(element) is not { } id || _byId[id] != element)
            {
                into.Add(new XElement(element));
            }
            else if (keep(id))
            {
                into.Add(new XElement(element.Name, new XAttribute("id", id)));
            }
        }
    }
}
