using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Mgb;

namespace JackAll.Core.Mods;

/// <summary>
/// A Magma UI package (`.mgb`) as an <see cref="IContainerSplitter"/>: one fragment per top-level
/// area, plus the material list, the string table and the exported-object table under reserved ids.
/// </summary>
/// <remarks>
/// A fragment is the area as <see cref="MgbXml"/> renders it, staged as
/// <c>&lt;name&gt;.&lt;name hash decimal&gt;.xml</c>. Every name in a package is unique within its
/// scope - areas in the package, elements in an area, materials, exports and strings - so a merge
/// matches children by name and two mods adding to one page both land. The pool counts and the
/// distinct-texture count are derived from content, so no fragment carries them. See
/// docs/docs/file-formats/mgb.md.
/// </remarks>
public sealed class MgbContainerSplitter : IContainerSplitter
{
    private const string Extension = ".mgb";
    private const string AreasElement = "CHILDREN";
    private const string MaterialsElement = "MATERIALS";
    private const string DistinctTexturesAttribute = "materialExtra";
    private const string Indent = "  ";

    /// <summary>The package-level lists that are a fragment each, by reserved id, in file order.</summary>
    private static readonly (string Id, string Element)[] Sections =
    [
        ("_materials.xml", MaterialsElement),
        ("_strings.xml", "STRINGTABLE"),
        ("_exports.xml", "GENERICOBJECTTABLE"),
    ];

    public static MgbContainerSplitter Instance { get; } = new();

    public static bool IsPackage(string fileName) => fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The id an area is staged under, labelled by its name when the package spells one.</summary>
    public static string IdOf(XElement area)
    {
        string name = NameOf(area);
        return FragmentId.Of(MgbXmlValue.ParseName(name), name.StartsWith('#') ? null : name);
    }

    public IContainerTree Open(byte[] container) => Open(container, []);

    /// <summary>A package with <paramref name="names"/> spelled out where it keeps only their hashes -
    /// the names its XML source declared.</summary>
    public IContainerTree Open(byte[] container, IEnumerable<string> names)
        => new Tree(XElement.Parse(MgbXml.ToXml(MgbPackage.Read(container), names)));

    public string Canonicalize(string fragmentId, string fragmentXml) => Render(XElement.Parse(fragmentXml));

    /// <summary>An action executer is decided whole: its events index its actions by position.</summary>
    public (string Merged, IReadOnlyList<string> Conflicts) Merge(string fragmentId, string ancestor, string ours, string theirs)
        => XmlListMerge.MergeTree(ancestor, ours, theirs, KeyOf, whole: e => e.Name.LocalName == "ACTIONEXECUTER");

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        XElement package = XElement.Parse(MgbXml.Decode(baseBytes));
        XElement areas = package.Element(AreasElement)
            ?? throw new InvalidDataException("This package has no area list.");
        Dictionary<uint, XElement> byNumber = areas.Elements().ToDictionary(NumberOf);
        bool materialsChanged = false;

        foreach ((string id, string xml) in fragmentXmlById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            XElement fragment = XElement.Parse(xml);
            if (SectionOf(id) is { } section)
            {
                if (fragment.Name.LocalName != section)
                {
                    throw new InvalidDataException(
                        $"A fragment staged as '{id}' holds a <{fragment.Name.LocalName}>, not the <{section}> that id names.");
                }

                Place(package, section, fragment);
                materialsChanged |= section == MaterialsElement;
                continue;
            }

            uint number = FragmentId.NumberOf(id)
                ?? throw new InvalidDataException($"'{id}' names neither an area nor a package section.");
            if (fragment.Name.LocalName != "Area" || NumberOf(fragment) != number)
            {
                throw new InvalidDataException(
                    $"A fragment staged as '{id}' holds a <{fragment.Name.LocalName}> named '{NameOf(fragment)}'. "
                    + (fragment.Name.LocalName == "Area" ? $"Name the file '{IdOf(fragment)}', or fix the name it holds." : ""));
            }

            if (byNumber.TryGetValue(number, out XElement? existing))
            {
                existing.ReplaceWith(fragment);
            }
            else
            {
                areas.Add(fragment);
            }
            byNumber[number] = fragment;
        }

        // The engine is handed the number of distinct texture paths, so an edited list re-derives it.
        if (materialsChanged)
        {
            XElement materials = package.Element(MaterialsElement)!;
            materials.SetAttributeValue(DistinctTexturesAttribute,
                materials.Elements().Select(m => (string?)m.Attribute("texture")).Distinct(StringComparer.Ordinal).Count());
        }

        MgbPools.Raise(package);
        return MgbXml.Encode(package.ToString());
    }

    /// <summary>A section in place of the package's own, or added where the file keeps it.</summary>
    private static void Place(XElement package, string section, XElement fragment)
    {
        if (package.Element(section) is { } existing)
        {
            existing.ReplaceWith(fragment);
        }
        else if (Sections.SkipWhile(s => s.Element != section).Skip(1)
                     .Select(s => package.Element(s.Element)).OfType<XElement>().FirstOrDefault() is { } next)
        {
            next.AddBeforeSelf(fragment);
        }
        else
        {
            package.Add(fragment);
        }
    }

    private static string? SectionOf(string fragmentId)
        => Sections.FirstOrDefault(s => s.Id.Equals(fragmentId, StringComparison.OrdinalIgnoreCase)).Element;

    /// <summary>What a child is matched by in a merge: the name of what it is, else its tag.</summary>
    private static string KeyOf(XElement child)
    {
        string? name = (string?)child.Element("USERDATA")?.Attribute("name")
            ?? (string?)child.Attribute("name")
            ?? (string?)child.Attribute("key");
        return name is null ? child.Name.LocalName : $"{child.Name.LocalName}|{name}";
    }

    private static string NameOf(XElement area) => (string?)area.Element("USERDATA")?.Attribute("name") ?? "";

    private static uint NumberOf(XElement area) => MgbXmlValue.ParseName(NameOf(area));

    /// <summary>One fragment as staged, without the count the build derives.</summary>
    private static string Render(XElement fragment)
    {
        var copy = new XElement(fragment);
        copy.Attribute(DistinctTexturesAttribute)?.Remove();
        return FragmentXml.Render(copy, Indent);
    }

    private sealed class Tree(XElement package) : IContainerTree
    {
        private readonly Dictionary<string, XElement> _byId = Index(package);

        public string? Extract(string fragmentId)
        {
            string key = SectionOf(fragmentId) is null && FragmentId.NumberOf(fragmentId) is { } number
                ? FragmentId.Of(number)
                : fragmentId;
            return _byId.TryGetValue(key, out XElement? fragment) ? Render(fragment) : null;
        }

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _byId.Values.Select(e => new FcbFragmentInfo(
                e.Name.LocalName == "Area" ? IdOf(e) : Sections.First(s => s.Element == e.Name.LocalName).Id,
                e.ToString(SaveOptions.DisableFormatting).Length))];

        /// <summary>The package with every kept fragment reduced to a marker, the rest dropped, and the
        /// derived counts removed - so an importer can tell a changed fragment from a change around them.</summary>
        public string? Skeleton(Func<string, bool> keep)
        {
            var clone = new XElement(package);
            clone.Attribute("POOLCOUNTS")?.Remove();
            foreach ((string id, string element) in Sections)
            {
                clone.Element(element)?.ReplaceWith(keep(id) ? new XElement(element) : null);
            }

            XElement areas = clone.Element(AreasElement)!;
            areas.ReplaceNodes(areas.Elements()
                .Where(a => keep(IdOf(a)))
                .Select(a => new XElement("Area", new XAttribute("id", FragmentId.Of(NumberOf(a))))));
            return clone.ToString();
        }

        /// <summary>Every fragment, an area under its bare number so any label finds it.</summary>
        private static Dictionary<string, XElement> Index(XElement package)
        {
            var byId = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            foreach ((string id, string element) in Sections)
            {
                if (package.Element(element) is { } section)
                {
                    byId[id] = section;
                }
            }
            foreach (XElement area in package.Element(AreasElement)?.Elements() ?? [])
            {
                byId[FragmentId.Of(NumberOf(area))] = area;
            }
            return byId;
        }
    }
}
