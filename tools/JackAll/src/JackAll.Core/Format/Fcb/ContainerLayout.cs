using System.Globalization;
using System.Xml.Linq;

namespace JackAll.Core.Format.Fcb;

/// <summary>One mission layer a sector must have, and which entities must live under it.</summary>
/// <param name="Before">The layer this one is inserted ahead of when it has to be created, or null to
/// put it after the last existing layer. Shipped sectors end with <c>main</c> and the community's
/// outpost mods prepend theirs, so where a new layer lands is recorded rather than guessed.</param>
/// <param name="Values">Header values besides the two path fields, carried verbatim. Empty for every
/// layer in the retail corpus and in the mods built on it.</param>
/// <param name="Under">The level cell this layer hangs off, as its type hash in hex - <c>mapsdata</c>
/// groups its layers one level down. Null everywhere else, where layers sit on the root itself.</param>
public sealed record LayerSpec(
    string Path,
    uint PathId,
    string? Before,
    IReadOnlyList<(uint Hash, byte[] Bytes)> Values,
    IReadOnlyList<ulong> Entities,
    string? Under = null)
{
    /// <summary>What identifies this layer in a container. A path alone is not enough: mapsdata holds
    /// one <c>main</c> per level cell - 25 of them in world1 - so there the cell qualifies it.</summary>
    public string Key => KeyOf(Under, Path);

    /// <summary>The one spelling of that key, since it is the layout format's wire contract.</summary>
    public static string KeyOf(string? under, string path) => under is null ? path : under + "\\" + path;
}

/// <summary>
/// What a container needs said about it beyond its own fragments: which mission layer each placed
/// entity belongs to, and what to remove. A fragment id carries no layer and an override always lands
/// where the base container already put it, so neither can be expressed one fragment at a time.
/// </summary>
/// <remarks>
/// Staged as the reserved fragment id <c>_layout.xml</c> beside the container's own fragments, the
/// same way a MOVE graph's manager sections are (see <see cref="Move.MoveSections"/>). It reads as a
/// set of constraints, not a full picture: anything unlisted stays where the base container has it,
/// so applying a container's own layout to itself is a no-op. Structural membership is what the engine
/// spawns from - see docs/docs/engine-internals/entity-instancing.md.
/// </remarks>
public sealed class ContainerLayout(
    IReadOnlyList<LayerSpec> layers,
    IReadOnlyList<string>? removed = null,
    IReadOnlyList<string>? deleted = null)
{
    /// <summary>The reserved fragment id. The leading underscore keeps it out of the entity id space,
    /// whose ids all end in a number.</summary>
    public const string Id = "_layout.xml";

    private const string LayoutElement = "layout";

    public IReadOnlyList<LayerSpec> Layers { get; } = layers;

    /// <summary>
    /// Layers the sector should no longer have. Only ever honoured for a layer left with no entities,
    /// so this drops an empty grouping and can never delete content - the outpost mods repurpose a
    /// mission layer they found, which reads as its old name going away and a new one arriving.
    /// </summary>
    public IReadOnlyList<string> Removed { get; } = removed ?? [];

    /// <summary>
    /// Fragments the container should no longer have at all, as their ids. The one operation in this
    /// document that cannot merge: two mods that disagree about whether something exists genuinely
    /// disagree, so a deletion is exclusive over that one fragment - see <see cref="Contested"/> -
    /// rather than over the whole container, which is what a whole-file override would cost.
    /// </summary>
    public IReadOnlyList<string> Deleted { get; } = deleted ?? [];

    /// <summary>
    /// The fragments this layout deletes that <paramref name="stagedFragmentIds"/> also overrides.
    /// Nobody can honour both, so the override wins and the fragment stays: keeping something a mod
    /// wanted gone is the lesser harm, and it is the same call the string table already makes.
    /// </summary>
    public IReadOnlyList<string> Contested(IEnumerable<string> stagedFragmentIds)
    {
        if (Deleted.Count == 0)
        {
            return [];
        }

        var staged = new HashSet<string>(stagedFragmentIds, FcbFragments.IdComparer);
        return [.. Deleted.Where(staged.Contains)];
    }

    public static bool IsLayoutId(string fragmentId)
        => string.Equals(fragmentId, Id, StringComparison.OrdinalIgnoreCase);

    /// <summary>How a <c>mapsdata</c> level cell is named in a layout. The cell carries no values at
    /// all, so its type hash is the only identity it has, and 19 of the shipped ones hash from a name
    /// nobody has recovered - hence the raw hash rather than a readable name.</summary>
    public static string CellKey(FcbObject cell) => cell.TypeHash.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>The layer <paramref name="entityId"/> is listed under, or null when this document says
    /// nothing about it and the base container's own placement stands.</summary>
    public string? LayerOf(ulong entityId)
        => (_placement ??= PlacementByEntity()).GetValueOrDefault(entityId);

    private Dictionary<ulong, string>? _placement;

    /// <summary>Every layer of a decoded sector and every addressable entity under it, in document
    /// order.</summary>
    public static ContainerLayout Of(FcbObject root)
    {
        var byLayer = new Dictionary<FcbObject, List<ulong>>(ReferenceEqualityComparer.Instance);
        foreach (FcbFragments.FragmentSlot slot in FcbFragments.Slots(root))
        {
            if (slot.Parent.TypeHash != WorldHashes.MissionLayer)
            {
                continue;
            }
            if (!byLayer.TryGetValue(slot.Parent, out List<ulong>? entities))
            {
                byLayer[slot.Parent] = entities = [];
            }
            entities.Add(FcbEntityFields.ReadU64(slot.Node, WorldHashes.DisEntityId));
        }

        List<LayerSpec> layers = [];
        foreach ((string? under, FcbObject layer) in FcbFragments.KeyedLayersOf(root))
        {
            string path = MissionLayers.NameOf(layer);
            layers.Add(new LayerSpec(
                path,
                MissionLayers.PathIdOf(layer) ?? NameHash.Compute(path),
                Before: null,
                Values: [.. layer.Values
                    .Where(v => v.Key != WorldHashes.TextPathId && v.Key != WorldHashes.PathId)
                    .Select(v => (v.Key, v.Value))],
                Entities: byLayer.TryGetValue(layer, out List<ulong>? found) ? found : [],
                Under: under));
        }

        return new ContainerLayout(layers);
    }

    /// <summary>
    /// What has to be said to turn <paramref name="vanillaRoot"/> into <paramref name="targetRoot"/>
    /// beyond its own fragments: the layers that are new, every entity that changed layer, and every
    /// fragment that is gone. Null when the two already agree. Taken from the roots rather than two
    /// layouts because a deletion is a fragment-id difference, which a container with no mission
    /// layers - an entity library - has just as much as a sector does.
    /// </summary>
    public static ContainerLayout? Diff(FcbObject vanillaRoot, FcbObject targetRoot)
    {
        ContainerLayout vanilla = Of(vanillaRoot);
        ContainerLayout target = Of(targetRoot);
        Dictionary<ulong, string> before = vanilla.PlacementByEntity();
        var vanillaKeys = new HashSet<string>(vanilla.Layers.Select(l => l.Key), StringComparer.OrdinalIgnoreCase);

        List<LayerSpec> changed = [];
        for (int i = 0; i < target.Layers.Count; i++)
        {
            LayerSpec layer = target.Layers[i];
            bool isNew = !vanillaKeys.Contains(layer.Key);
            ulong[] moved = [.. layer.Entities.Where(id =>
                !before.TryGetValue(id, out string? was)
                || !was.Equals(layer.Key, StringComparison.OrdinalIgnoreCase))];

            if (!isNew && moved.Length == 0)
            {
                continue;
            }

            // A layer worth mentioning states its whole contents, in order. Listing only what moved
            // would leave the ones that did not sitting ahead of it, which is a different container.
            changed.Add(layer with
            {
                // Only a layer being created needs a position, and it is the first layer after it
                // that the base container already has, in the same cell.
                Before = isNew
                    ? target.Layers.Skip(i + 1).FirstOrDefault(l => l.Under == layer.Under && vanillaKeys.Contains(l.Key))?.Path
                    : null,
            });
        }

        var targetKeys = new HashSet<string>(target.Layers.Select(l => l.Key), StringComparer.OrdinalIgnoreCase);
        string[] gone = [.. vanilla.Layers.Where(l => !targetKeys.Contains(l.Key)).Select(l => l.Key)];

        // Keyed on fragment ids, not placement, so this sees an archetype a library dropped as
        // readily as an entity a sector did.
        var kept = new HashSet<string>(
            FcbFragments.List(targetRoot).Select(f => f.Id), FcbFragments.IdComparer);
        string[] deleted = [.. FcbFragments.List(vanillaRoot)
            .Select(f => f.Id)
            .Where(id => !kept.Contains(id))];

        return changed.Count == 0 && gone.Length == 0 && deleted.Length == 0
            ? null
            : new ContainerLayout(changed, gone, deleted);
    }

    /// <summary>
    /// Two layouts folded against their common ancestor. Layers union; an entity both sides moved, to
    /// different layers, is the one real conflict, since it can only have one parent.
    /// </summary>
    public static (ContainerLayout Merged, bool Conflict) Merge(
        ContainerLayout ancestor, ContainerLayout ours, ContainerLayout theirs)
    {
        Dictionary<ulong, string> baseline = ancestor.PlacementByEntity();
        Dictionary<ulong, string> mine = ours.PlacementByEntity();
        Dictionary<ulong, string> yours = theirs.PlacementByEntity();

        bool conflict = false;
        var placements = new Dictionary<ulong, string>();
        foreach (ulong id in mine.Keys.Union(yours.Keys))
        {
            mine.TryGetValue(id, out string? a);
            yours.TryGetValue(id, out string? b);

            if (a is not null && b is not null && !a.Equals(b, StringComparison.OrdinalIgnoreCase))
            {
                // Both moved it, and not to the same place. Load order picks, exactly as it does for
                // a whole-file override, and the caller is handed the fact that it happened.
                conflict = true;
            }

            string winner = b ?? a!;
            if (!baseline.TryGetValue(id, out string? was) || !winner.Equals(was, StringComparison.OrdinalIgnoreCase))
            {
                placements[id] = winner;
            }
        }

        Dictionary<string, List<ulong>> byLayer = [];
        foreach ((ulong id, string path) in placements)
        {
            if (!byLayer.TryGetValue(path, out List<ulong>? ids))
            {
                byLayer[path] = ids = [];
            }
            ids.Add(id);
        }

        var ancestorKeys = new HashSet<string>(
            ancestor.Layers.Select(l => l.Key), StringComparer.OrdinalIgnoreCase);

        List<LayerSpec> merged = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Higher priority first, so that where both declare one layer its header is the one load
        // order would pick - the same rule the placements above already follow.
        foreach (LayerSpec layer in theirs.Layers.Concat(ours.Layers))
        {
            if (!seen.Add(layer.Key))
            {
                continue;
            }

            ulong[] entities = byLayer.TryGetValue(layer.Key, out List<ulong>? ids) ? [.. ids.Order()] : [];

            // A layer with nothing left to say still has to be stated when it is one the mods add,
            // or applying the merge would not create it.
            if (entities.Length > 0 || !ancestorKeys.Contains(layer.Key))
            {
                merged.Add(layer with { Entities = entities });
            }
        }

        string[] removed = [.. ours.Removed.Union(theirs.Removed, StringComparer.OrdinalIgnoreCase)];

        // A deletion one side makes and the other contradicts by re-filing that entity is the same
        // collision Contested describes, decided the same way: the entity stays, and the caller is
        // told. Deletions nobody contradicts simply union.
        var placedIds = new HashSet<string>(
            placements.Keys.Select(FcbFragments.EntityFragmentId), FcbFragments.IdComparer);
        string[] union = [.. ours.Deleted.Union(theirs.Deleted, FcbFragments.IdComparer)];
        string[] deleted = [.. union.Where(id => !placedIds.Contains(id)).Order(StringComparer.Ordinal)];
        conflict |= deleted.Length != union.Length;

        return (new ContainerLayout(merged, removed, deleted), conflict);
    }

    private Dictionary<ulong, string> PlacementByEntity()
    {
        var placement = new Dictionary<ulong, string>();
        foreach (LayerSpec layer in Layers)
        {
            foreach (ulong id in layer.Entities)
            {
                placement[id] = layer.Key;
            }
        }
        return placement;
    }

    public static ContainerLayout Parse(string xml)
    {
        XElement root = XDocument.Parse(xml).Root
            ?? throw new InvalidDataException("Empty mission-layer layout document.");
        if (!root.Name.LocalName.Equals(LayoutElement, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"A mission-layer layout is a <{LayoutElement}> document, not <{root.Name.LocalName}>.");
        }

        string[] removed = [.. root.Elements("remove")
            .Select(e => (string?)e.Attribute("path") ?? "")
            .Where(p => p.Length > 0)];

        string[] deleted = [.. root.Elements("delete")
            .Select(ParseDeleted)
            .Distinct(FcbFragments.IdComparer)];

        List<LayerSpec> layers = [];
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placed = new Dictionary<ulong, string>();
        foreach (XElement element in root.Elements("layer"))
        {
            string path = (string?)element.Attribute("path") ?? "";
            if (path.Length == 0)
            {
                throw new InvalidDataException("A <layer> in the layout names no path.");
            }

            string? cell = (string?)element.Attribute("under");
            string key = LayerSpec.KeyOf(cell, path);
            if (!keys.Add(key))
            {
                throw new InvalidDataException($"The layout lists mission layer '{key}' twice.");
            }

            List<ulong> entities = [];
            foreach (XElement entity in element.Elements("entity"))
            {
                ulong id = ParseEntityId((string?)entity.Attribute("id"), path);
                if (placed.TryGetValue(id, out string? already))
                {
                    throw new InvalidDataException(
                        $"The layout puts entity {id} under both '{already}' and '{key}'; it can only have one.");
                }
                placed[id] = key;
                entities.Add(id);
            }

            layers.Add(new LayerSpec(
                path,
                ParsePathId((string?)element.Attribute("pathId")) ?? NameHash.Compute(path),
                (string?)element.Attribute("before"),
                [.. element.Elements("value").Select(ParseValue)],
                entities,
                cell));
        }

        var placedIds = new Dictionary<string, string>(FcbFragments.IdComparer);
        foreach ((ulong id, string under) in placed)
        {
            placedIds[FcbFragments.EntityFragmentId(id)] = under;
        }

        foreach (string id in deleted)
        {
            if (placedIds.TryGetValue(id, out string? under))
            {
                throw new InvalidDataException(
                    $"The layout both deletes {id} and puts it under '{under}'.");
            }
        }

        return new ContainerLayout(layers, removed, deleted);
    }

    /// <summary>The canonical text, which is what a three-way merge compares.</summary>
    public string Render()
    {
        var root = new XElement(LayoutElement);
        foreach (string path in Removed)
        {
            root.Add(new XElement("remove", new XAttribute("path", path)));
        }

        foreach (string id in Deleted)
        {
            root.Add(new XElement(
                "delete",
                IsNumericId(id)
                    ? new XAttribute("id", id[..^4])
                    : new XAttribute("path", id)));
        }

        foreach (LayerSpec layer in Layers)
        {
            var element = new XElement(
                "layer",
                new XAttribute("path", layer.Path),
                new XAttribute("pathId", layer.PathId.ToString("X8", CultureInfo.InvariantCulture)));
            if (layer.Under is { } cell)
            {
                element.Add(new XAttribute("under", cell));
            }
            if (layer.Before is { } before)
            {
                element.Add(new XAttribute("before", before));
            }
            foreach ((uint hash, byte[] bytes) in layer.Values)
            {
                element.Add(new XElement(
                    "value",
                    new XAttribute("hash", hash.ToString("X8", CultureInfo.InvariantCulture)),
                    Convert.ToHexString(bytes)));
            }
            foreach (ulong id in layer.Entities)
            {
                element.Add(new XElement("entity", new XAttribute("id", id.ToString(CultureInfo.InvariantCulture))));
            }
            root.Add(element);
        }

        return FragmentXml.Render(root, "  ");
    }

    /// <summary>
    /// One <c>&lt;delete&gt;</c>, as the fragment id it names. A placed entity is spelled
    /// <c>id="2054…"</c> and an archetype <c>path="weapons\Grenades\M67.xml"</c> - two attributes
    /// rather than one, so a mistyped entity id fails loudly instead of quietly becoming a path that
    /// matches nothing.
    /// </summary>
    private static string ParseDeleted(XElement element)
    {
        if ((string?)element.Attribute("path") is { Length: > 0 } path)
        {
            return path.Trim();
        }

        return FcbFragments.EntityFragmentId(ParseEntityId((string?)element.Attribute("id"), "a <delete>"));
    }

    /// <summary>Whether this id is a placed entity's, which is what decides how
    /// <see cref="Render"/> spells it back out.</summary>
    private static bool IsNumericId(string fragmentId)
        => fragmentId.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
        && fragmentId.Length > 4
        && !fragmentId.AsSpan(0, fragmentId.Length - 4).ContainsAnyExceptInRange('0', '9');

    /// <summary>Accepts a bare <c>disEntityId</c> or the fragment id spelling of one, so a layout can
    /// be written by copying a fragment's own filename.</summary>
    private static ulong ParseEntityId(string? text, string where)
        => TryParseEntityId((text ?? "").Trim(), out ulong id)
            ? id
            : throw new InvalidDataException($"'{text}' in {where} is not an entity id.");

    private static bool TryParseEntityId(string text, out ulong id)
    {
        string canonical = FcbFragments.Canonicalize(text);
        if (canonical.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            canonical = canonical[..^4];
        }

        return ulong.TryParse(canonical, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    private static uint? ParsePathId(string? text)
        => text is null
            ? null
            : uint.TryParse(text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id)
                ? id
                : throw new InvalidDataException($"'{text}' is not a hexadecimal mission-layer id.");

    private static (uint Hash, byte[] Bytes) ParseValue(XElement value)
    {
        string hash = (string?)value.Attribute("hash") ?? "";
        return uint.TryParse(hash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint parsed)
            ? (parsed, Convert.FromHexString(value.Value.Trim()))
            : throw new InvalidDataException($"A layout <value> has no readable hash attribute ('{hash}').");
    }
}
