using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;

namespace JackAll.Core.Mods;

/// <summary>
/// A world's particle library (<c>*_deploadnewparticles.rml</c>) as an <see cref="IContainerSplitter"/>:
/// one fragment per <c>PartSys</c>, numbered by the <c>CPathID</c> of its name - the resource id the
/// engine finds it by - and labelled with the name. Emitters merge attribute by attribute, a curve whole.
/// </summary>
public sealed class ParticleLibraryContainerSplitter : IContainerSplitter
{
    public const string Suffix = "_deploadnewparticles.rml";

    private const string SystemElement = "PartSys";
    private const string NameAttribute = "Name";

    public static ParticleLibraryContainerSplitter Instance { get; } = new();

    public static bool IsParticleLibrary(string path) => path.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>A particle system's id, <c>weapons.weapons.pl_muzzleflash_ak47.483129128.xml</c>.</summary>
    public static string IdOf(string systemName) => FragmentId.Of(NameHash.Compute(systemName), systemName);

    public IContainerTree Open(byte[] container) => new Tree(RmlDocument.Deserialize(container));

    public string Canonicalize(string fragmentId, string fragmentXml) => Render(XElement.Parse(fragmentXml));

    public (string Merged, IReadOnlyList<string> Conflicts) Merge(string fragmentId, string ancestor, string ours, string theirs)
        => XmlListMerge.MergeTree(ancestor, ours, theirs, KeyOf, whole: IsCurve);

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        XElement root = RmlDocument.Deserialize(baseBytes);
        Dictionary<uint, XElement> byNumber = Index(root);
        foreach ((string id, string xml) in fragmentXmlById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            XElement replacement = XElement.Parse(xml);
            string name = (string?)replacement.Attribute(NameAttribute) ?? "";
            uint number = NameHash.Compute(name);
            if (replacement.Name.LocalName != SystemElement || name.Length == 0 || FragmentId.NumberOf(id) != number)
            {
                throw new InvalidDataException(
                    $"A particle fragment staged as '{id}' holds a <{replacement.Name.LocalName}> named '{name}'. "
                    + $"Name the file '{(name.Length == 0 ? id : IdOf(name))}', or fix the name it holds.");
            }

            if (byNumber.TryGetValue(number, out XElement? existing))
            {
                existing.ReplaceWith(replacement);
            }
            else
            {
                root.Add(replacement);
            }
            byNumber[number] = replacement;
        }

        return RmlDocument.Serialize(root);
    }

    /// <summary>An emitter is matched by its name, anything else by its element name.</summary>
    private static string KeyOf(XElement child)
        => (string?)child.Attribute(NameAttribute) is { } name ? $"{child.Name.LocalName}|{name}" : child.Name.LocalName;

    /// <summary>A keyframed value (<c>InitSizeKF</c>, <c>PartColorKF</c>): its knots only mean something together.</summary>
    private static bool IsCurve(XElement element) => element.Name.LocalName.EndsWith("KF", StringComparison.Ordinal);

    private static Dictionary<uint, XElement> Index(XElement root)
    {
        var byNumber = new Dictionary<uint, XElement>();
        foreach (XElement system in root.Elements(SystemElement))
        {
            if ((string?)system.Attribute(NameAttribute) is { Length: > 0 } name)
            {
                byNumber[NameHash.Compute(name)] = system;
            }
        }
        return byNumber;
    }

    private static string Render(XElement system) => FragmentXml.Render(system, "\t");

    private sealed class Tree(XElement root) : IContainerTree
    {
        private readonly Dictionary<uint, XElement> _byNumber = Index(root);

        public string? Extract(string fragmentId)
            => FragmentId.NumberOf(fragmentId) is { } number && _byNumber.TryGetValue(number, out XElement? system)
                ? Render(system)
                : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _byNumber.Select(kv => new FcbFragmentInfo(
                FragmentId.Of(kv.Key, (string)kv.Value.Attribute(NameAttribute)!), kv.Value.ToString(SaveOptions.DisableFormatting).Length))];

        /// <summary>The library with every kept system reduced to a marker and the rest dropped.</summary>
        public string? Skeleton(Func<string, bool> keep)
        {
            var clone = new XElement(root.Name, root.Attributes());
            foreach (XElement child in root.Elements())
            {
                if (child.Name.LocalName != SystemElement || (string?)child.Attribute(NameAttribute) is not { Length: > 0 } name)
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
