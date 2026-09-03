using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Format.Rml;

namespace JackAll.Core.Mods;

/// <summary>
/// A world or map descriptor (`&lt;world&gt;.game.xml`) as an <see cref="IContainerSplitter"/>: one
/// fragment per mission.
/// </summary>
/// <remarks>
/// Mission names are unique, and the flat <c>&lt;MissionLayers&gt;</c> index is those same layers in
/// the same order - derived rather than authored, so <see cref="Apply"/> rebuilds it instead of
/// asking a mod to maintain two copies. A change anywhere else in the file keeps its whole-file
/// override. See docs/design/mod-layout-final.md.
/// </remarks>
public sealed class WorldDescriptorContainerSplitter : IContainerSplitter
{
    private const string MissionsDefElement = "MissionsDef";
    private const string MissionsElement = "Missions";
    private const string MissionLayersElement = "MissionLayers";
    private const string MissionElement = "Mission";
    private const string LayerElement = "Layer";
    private const string NameAttribute = "Name";

    public static WorldDescriptorContainerSplitter Instance { get; } = new();

    /// <summary>Whether this file is a world or map descriptor.</summary>
    public static bool IsWorldDescriptor(string fileName)
        => fileName.EndsWith(".game.xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A mission's id: its own name mapped onto a path, one directory per segment, the way an
    /// archetype's dotted name becomes one. Shipped names are unique and carry no dot, so nothing
    /// here collides with the <c>&lt;name&gt;.&lt;digits&gt;</c> spelling an entity id uses.
    /// </summary>
    public static string IdOf(string missionName)
        => string.Join('\\', missionName.Split('/', '\\')
            .Where(s => s.Length > 0)
            .Select(FcbFragments.Sanitize)) + ".xml";

    public IContainerTree Open(byte[] container) => new Tree(Decode(container));

    /// <summary>
    /// The compiled form only. One shipped descriptor (<c>tmpla</c>, the multiplayer template) is
    /// plain text instead, and re-serializing that would have to reproduce someone else's
    /// indentation to leave an untouched file untouched. It keeps its whole-file override.
    /// </summary>
    private static XElement Decode(byte[] container)
        => RmlDocument.TryDeserialize(container, out XElement? root)
            ? root
            : throw new InvalidDataException(
                "this descriptor is plain XML rather than the compiled .rml form, which is the only "
                + "one that splits per mission");

    public string Canonicalize(string fragmentId, string fragmentXml)
        => Render(XElement.Parse(fragmentXml));

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        XElement root = Decode(baseBytes);
        XElement missions = MissionsOf(root)
            ?? throw new InvalidDataException(
                "This descriptor has no <MissionsDef><Missions>, so it declares no mission to override.");
        Dictionary<string, XElement> byId = IndexMissions(root);

        foreach ((string id, string xml) in fragmentXmlById.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            XElement replacement = XElement.Parse(xml);
            string name = (string?)replacement.Attribute(NameAttribute) ?? "";
            if (name.Length == 0 || !FcbFragments.IdComparer.Equals(IdOf(name), id))
            {
                throw new InvalidDataException(
                    $"A mission fragment staged as '{id}' calls itself '{name}'. Name the file "
                    + $"'{(name.Length == 0 ? id : IdOf(name))}', or fix the mission it names.");
            }

            if (byId.TryGetValue(id, out XElement? existing))
            {
                existing.ReplaceWith(replacement);
            }
            else
            {
                missions.Add(replacement);
            }
            byId[id] = replacement;
        }

        RebuildIndex(root, missions);
        return RmlDocument.Serialize(root);
    }

    /// <summary>
    /// Rewrites the flat layer index from the missions themselves. It is a second copy of data the
    /// missions already carry, so a mod states its mission once and this keeps the two in step.
    /// </summary>
    private static void RebuildIndex(XElement root, XElement missions)
    {
        if (root.Element(MissionsDefElement)?.Element(MissionLayersElement) is not { } index)
        {
            return;
        }

        index.RemoveNodes();
        foreach (XElement layer in missions.Elements(MissionElement).SelectMany(LayersOf))
        {
            index.Add(new XElement(layer));
        }
    }

    private static IEnumerable<XElement> LayersOf(XElement mission)
        => mission.Elements("Layers").SelectMany(l => l.Elements(LayerElement));

    private static XElement? MissionsOf(XElement root)
        => root.Element(MissionsDefElement)?.Element(MissionsElement);

    /// <summary>Every mission of a descriptor under the id a mod stages it at.</summary>
    private static Dictionary<string, XElement> IndexMissions(XElement root)
    {
        var byId = new Dictionary<string, XElement>(FcbFragments.IdComparer);
        foreach (XElement mission in MissionsOf(root)?.Elements(MissionElement) ?? [])
        {
            if ((string?)mission.Attribute(NameAttribute) is { Length: > 0 } name)
            {
                byId[IdOf(name)] = mission;
            }
        }
        return byId;
    }

    /// <summary>One mission in the shape every staged fragment is written in.</summary>
    private static string Render(XElement mission) => FragmentXml.Render(mission, "  ");

    private sealed class Tree : IContainerTree
    {
        private readonly XElement _root;
        private readonly Dictionary<string, XElement> _byId;

        public Tree(XElement root)
        {
            _root = root;
            _byId = IndexMissions(root);
        }

        public string? Extract(string fragmentId)
            => _byId.TryGetValue(fragmentId, out XElement? mission) ? Render(mission) : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _byId.Select(kv => new FcbFragmentInfo(kv.Key, Render(kv.Value).Length))];

        /// <summary>
        /// The descriptor with every mission reduced to a marker naming it, so an importer can tell
        /// "these missions changed" from "something else in the file did". The flat layer index goes
        /// with them: it is rebuilt from the missions, so comparing it as well would report one
        /// change twice.
        /// </summary>
        public string? Skeleton(Func<string, bool> keep)
        {
            var clone = new XElement(_root);
            if (clone.Element(MissionsDefElement)?.Element(MissionLayersElement) is { } index)
            {
                index.RemoveNodes();
            }

            foreach (XElement mission in (MissionsOf(clone)?.Elements(MissionElement) ?? []).ToList())
            {
                string id = IdOf((string?)mission.Attribute(NameAttribute) ?? "");
                if (keep(id))
                {
                    mission.ReplaceWith(new XElement(MissionElement, new XAttribute(NameAttribute, id)));
                }
                else
                {
                    mission.Remove();
                }
            }

            return clone.ToString();
        }
    }
}
