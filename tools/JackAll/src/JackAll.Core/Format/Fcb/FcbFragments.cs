using System.Buffers;
using System.Globalization;

namespace JackAll.Core.Format.Fcb;

/// <summary>One override unit of a splitting container: its path-shaped id and the node it names.</summary>
public readonly record struct FcbFragment(string Id, FcbObject Node);

/// <summary>
/// The fragment-override id space of a container `.fcb`: which nodes of a recognised tree shape are
/// individually overridable, and what path-shaped id (see docs/design/fcb-deep-fragments.md) each one
/// gets. Two shapes are recognised:
///
///   - An entity library (root <c>EntityLibraries</c> of <c>EntityLibrary</c> group children): one
///     fragment per <c>EntityPrototype</c>, keyed on its entity's <c>hidName</c> — the engine's own
///     archetype map key — with the dotted name mapped onto a path
///     (<c>vehicle\Land\DLC_Vehicle1_DLC1.xml</c>).
///   - A container that places entities in mission layers (see <see cref="IsLayerBearing"/>): a world
///     sector, or a world's <c>omnis</c>, <c>managers</c> or <c>mapsdata</c>, the last of which groups
///     its layers one level down under a node per level cell. One fragment per placed
///     <c>Entity</c> under any
///     <c>MissionLayer</c>, keyed <c>&lt;hidName&gt;.&lt;disEntityId&gt;.xml</c>. The trailing numeric
///     id is authoritative and the name prefix cosmetic — see <see cref="Canonicalize"/> — because
///     <c>hidName</c> is not unique in a sector while <c>disEntityId</c> is the stable identity
///     mission scripts reference. An entity with no <c>disEntityId</c> gets no fragment.
///
/// Anything else doesn't split, and an id naming no fragment is new content rather than an override.
/// </summary>
public static class FcbFragments
{
    private static readonly SearchValues<char> InvalidFileNameChars = SearchValues.Create(Path.GetInvalidFileNameChars());

    /// <summary>
    /// Compares fragment ids the way every override lookup must: case-insensitively, and with a
    /// <c>&lt;name&gt;.&lt;digits&gt;.xml</c> leaf reduced to its numeric id — so a mod that staged an
    /// entity override under a since-renamed <c>hidName</c> still matches, and two spellings of the
    /// same entity can never produce two competing overrides.
    /// </summary>
    public static IEqualityComparer<string> IdComparer { get; } = new CanonicalIdComparer();

    /// <summary>The form <see cref="IdComparer"/> compares, as a string: lowercased, with a
    /// numeric-tailed leaf's cosmetic name prefix stripped (<c>Guard_12.2058514.xml</c> →
    /// <c>2058514.xml</c>). Allocates, so the comparer itself compares the same two spans in place
    /// instead — it runs on every lookup across ~376,000 fragment rows.</summary>
    public static string Canonicalize(string fragmentId)
    {
        SplitCanonical(fragmentId, out ReadOnlySpan<char> directory, out ReadOnlySpan<char> leaf);
        return string.Concat(directory, leaf).ToLowerInvariant();
    }

    /// <summary>An id's two canonical parts: the directory part (everything up to and including the
    /// last separator) and the leaf with any cosmetic <c>&lt;name&gt;.</c> prefix ahead of a purely
    /// numeric id removed. The prefix only exists on a <c>&lt;name&gt;.&lt;digits&gt;.xml</c> leaf, so
    /// an archetype path id splits as itself.</summary>
    private static void SplitCanonical(
        string fragmentId, out ReadOnlySpan<char> directory, out ReadOnlySpan<char> leaf)
    {
        int leafStart = fragmentId.LastIndexOf('\\') + 1;
        directory = fragmentId.AsSpan(0, leafStart);
        leaf = fragmentId.AsSpan(leafStart);

        if (!leaf.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ReadOnlySpan<char> stem = leaf[..^4];
        int lastDot = stem.LastIndexOf('.');
        if (lastDot < 0)
        {
            return;
        }

        ReadOnlySpan<char> tail = stem[(lastDot + 1)..];
        if (tail.Length > 0 && !tail.ContainsAnyExceptInRange('0', '9'))
        {
            leaf = leaf[(lastDot + 1)..];
        }
    }

    /// <summary>Every fragment of <paramref name="root"/>, in document order. Empty when the root
    /// matches no recognised shape. Ids are unique under <see cref="IdComparer"/>: on a duplicate key
    /// (two declarations of one archetype in one library), only the last occurrence is addressable,
    /// matching the engine's own last-wins resolution.</summary>
    public static IReadOnlyList<FcbFragment> List(FcbObject root)
        => [.. Slots(root).Select(s => new FcbFragment(s.Id, s.Node))];

    /// <summary>The node <paramref name="fragmentId"/> names, or null when nothing matches.</summary>
    public static FcbObject? Find(FcbObject root, string fragmentId)
    {
        foreach (FragmentSlot slot in Slots(root))
        {
            if (IdComparer.Equals(slot.Id, fragmentId))
            {
                return slot.Node;
            }
        }

        return null;
    }

    /// <summary>Where <see cref="FcbAssembler.Apply"/> attaches content whose fragment id matched
    /// nothing — new content a mod adds. A new node in a world sector joins the <c>main</c> mission
    /// layer (falling back to the first layer); in a library, a new <c>EntityLibraryGroup</c> joins
    /// the root and anything else (a new archetype's prototype) joins the last group, since a
    /// non-group at the root would stop the whole container from splitting. An unrecognised root
    /// appends at the root.</summary>
    internal static FcbObject AppendTarget(FcbObject root, FcbObject addition)
    {
        if (IsLayerBearing(root))
        {
            FcbObject? first = null;
            foreach (FcbObject layer in LayersOf(root))
            {
                first ??= layer;
                if (MissionLayers.IsMain(MissionLayers.NameOf(layer)))
                {
                    return layer;
                }
            }
            return first ?? root;
        }

        if (addition.TypeHash != WorldHashes.EntityLibrary && IsLibraryOfGroups(root))
        {
            return root.Children[^1];
        }

        return root;
    }

    /// <summary>
    /// Whether this root places entities in mission layers - a world sector, a world's <c>omnis</c>,
    /// <c>managers</c> or <c>mapsdata</c>. All four hold the same <c>MissionLayer -> Entity</c>
    /// structure and split the same way.
    /// </summary>
    public static bool IsLayerBearing(FcbObject root)
        => root.TypeHash == WorldHashes.WorldSector
        || root.TypeHash == WorldHashes.Omnis
        || root.TypeHash == WorldHashes.Managers
        || root.TypeHash == WorldHashes.MapsData;

    /// <summary>The nodes a layer-bearing root hangs its mission layers off: itself, except in
    /// <c>mapsdata</c>, which groups them one level down under a node per level cell.</summary>
    public static IEnumerable<FcbObject> LayerParentsOf(FcbObject root)
        => root.TypeHash == WorldHashes.MapsData ? root.Children : [root];

    /// <summary>Every mission layer of a layer-bearing root, however deeply it groups them.</summary>
    public static IEnumerable<FcbObject> LayersOf(FcbObject root)
        => KeyedLayersOf(root).Select(x => x.Layer);

    /// <summary>Every mission layer with the level cell it hangs off, which is null when the root
    /// holds it directly - the walk anything addressing a layer by name has to do.</summary>
    public static IEnumerable<(string? Under, FcbObject Layer)> KeyedLayersOf(FcbObject root)
    {
        foreach (FcbObject parent in LayerParentsOf(root))
        {
            string? under = ReferenceEquals(parent, root) ? null : WorldSectorLayout.CellKey(parent);
            foreach (FcbObject layer in parent.Children)
            {
                if (layer.TypeHash == WorldHashes.MissionLayer)
                {
                    yield return (under, layer);
                }
            }
        }
    }

    /// <summary>A fragment's place in the tree, held as parent + index so a replacement can be
    /// written straight back into the same slot.</summary>
    internal readonly record struct FragmentSlot(string Id, FcbObject Parent, int Index)
    {
        /// <summary>The node currently in the slot. Only valid until something moves it - a caller
        /// that rearranges children has to take this first.</summary>
        public FcbObject Node => Parent.Children[Index];
    }

    internal static List<FragmentSlot> Slots(FcbObject root)
    {
        var slots = new List<FragmentSlot>();
        if (IsLibraryOfGroups(root))
        {
            foreach (FcbObject group in root.Children)
            {
                for (int i = 0; i < group.Children.Count; i++)
                {
                    FcbObject prototype = group.Children[i];
                    if (prototype.TypeHash != WorldHashes.EntityPrototype)
                    {
                        continue;
                    }
                    string name = FirstEntityName(prototype);
                    if (name.Length > 0)
                    {
                        slots.Add(new FragmentSlot(ArchetypeId(name), group, i));
                    }
                }
            }
        }
        else if (IsLayerBearing(root))
        {
            foreach (FcbObject layer in LayersOf(root))
            {
                for (int i = 0; i < layer.Children.Count; i++)
                {
                    FcbObject entity = layer.Children[i];
                    if (entity.TypeHash != WorldHashes.Entity
                        || !entity.Values.TryGetValue(WorldHashes.DisEntityId, out byte[]? idBytes)
                        || idBytes.Length < 8)
                    {
                        continue;
                    }
                    ulong disEntityId = BitConverter.ToUInt64(idBytes, 0);
                    slots.Add(new FragmentSlot(EntityId(FcbEntityFields.ReadString(entity, WorldHashes.HidName), disEntityId), layer, i));
                }
            }
        }

        return DedupKeepingLast(slots);
    }

    /// <summary>Duplicate ids keep only their last occurrence — see <see cref="List"/>.</summary>
    private static List<FragmentSlot> DedupKeepingLast(List<FragmentSlot> slots)
    {
        var seen = new HashSet<string>(slots.Count, IdComparer);
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (!seen.Add(slots[i].Id))
            {
                slots.RemoveAt(i);
            }
        }
        return slots;
    }

    /// <summary>An archetype's dotted <c>hidName</c> mapped onto a path id, one directory per
    /// namespace segment: <c>vehicle.Land.Jeep</c> → <c>vehicle\Land\Jeep.xml</c>.</summary>
    private static string ArchetypeId(string hidName)
    {
        string[] segments = hidName.Split('.');
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = segments[i].Length == 0 ? "_" : Sanitize(segments[i]);
        }
        return string.Join('\\', segments) + ".xml";
    }

    /// <summary>The canonical id of a placed entity's fragment — the bare name-less form, for a
    /// caller that only knows the <c>disEntityId</c> (the Map tab's entity → fragment-row jump);
    /// <see cref="IdComparer"/> matches it against the named form the rows carry.</summary>
    public static string EntityFragmentId(ulong disEntityId)
        => disEntityId.ToString(CultureInfo.InvariantCulture) + ".xml";

    private static string EntityId(string hidName, ulong disEntityId)
        => hidName.Length == 0
            ? EntityFragmentId(disEntityId)
            : Sanitize(hidName) + "." + EntityFragmentId(disEntityId);

    private static string FirstEntityName(FcbObject prototype)
    {
        foreach (FcbObject child in prototype.Children)
        {
            if (child.TypeHash == WorldHashes.Entity)
            {
                string name = FcbEntityFields.ReadString(child, WorldHashes.HidName);
                if (name.Length > 0)
                {
                    return name;
                }
            }
        }
        return "";
    }

    private static bool IsLibraryOfGroups(FcbObject root)
        => root.Values.Count == 0
        && root.TypeHash == WorldHashes.EntityLibraries
        && root.Children.Count > 0
        && root.Children.All(c => c.TypeHash == WorldHashes.EntityLibrary);

    /// <summary>A name made safe to use as one path segment of a fragment id.</summary>
    internal static string Sanitize(string name)
    {
        if (name.AsSpan().IndexOfAny(InvalidFileNameChars) < 0)
        {
            return name;
        }

        return string.Create(name.Length, name, static (chars, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                chars[i] = InvalidFileNameChars.Contains(source[i]) ? '_' : source[i];
            }
        });
    }

    private sealed class CanonicalIdComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y)
        {
            if (x is null || y is null)
            {
                return ReferenceEquals(x, y);
            }

            SplitCanonical(x, out ReadOnlySpan<char> xDirectory, out ReadOnlySpan<char> xLeaf);
            SplitCanonical(y, out ReadOnlySpan<char> yDirectory, out ReadOnlySpan<char> yLeaf);
            return xDirectory.Equals(yDirectory, StringComparison.OrdinalIgnoreCase)
                && xLeaf.Equals(yLeaf, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(string obj)
        {
            SplitCanonical(obj, out ReadOnlySpan<char> directory, out ReadOnlySpan<char> leaf);
            return HashCode.Combine(
                string.GetHashCode(directory, StringComparison.OrdinalIgnoreCase),
                string.GetHashCode(leaf, StringComparison.OrdinalIgnoreCase));
        }
    }
}
