using System.Buffers;
using System.Globalization;
using System.Text;

namespace JackAll.Core.Format.Fcb;

/// <summary>One override unit of a splitting container: its path-shaped id and the node it names.</summary>
public readonly record struct FcbFragment(string Id, FcbObject Node);

/// <summary>
/// The fragment-override id space of a container `.fcb`: which nodes of a recognised tree shape are
/// individually overridable, and what path-shaped id (see docs/design/fcb-deep-fragments.md) each one
/// gets. Two shapes are recognised:
///
///   - An entity library (root <c>EntityLibrary</c> of <c>EntityLibraryGroup</c> children): one
///     fragment per <c>EntityPrototype</c>, keyed on its entity's <c>hidName</c> — the engine's own
///     archetype map key — with the dotted name mapped onto a path
///     (<c>vehicle\Land\DLC_Vehicle1_DLC1.xml</c>).
///   - A world sector (root <c>WorldSector</c>): one fragment per placed <c>Entity</c> under any
///     <c>MissionLayer</c>, keyed <c>&lt;hidName&gt;.&lt;disEntityId&gt;.xml</c>. The trailing numeric
///     id is authoritative and the name prefix cosmetic — see <see cref="Canonicalize"/> — because
///     <c>hidName</c> is not unique in a sector while <c>disEntityId</c> is the stable identity
///     mission scripts reference. An entity with no <c>disEntityId</c> gets no fragment.
///
/// Anything else doesn't split. The pre-deep-fragment id space — one <c>NN_Name.xml</c> per library
/// group, which is still <see cref="FcbXml.ToXml"/>'s Gibbed-compatible multi-export naming — keeps
/// working as an alias: <see cref="Find"/> and <see cref="FcbAssembler.Apply"/> resolve a group id to
/// its whole group, so overrides staged by older JackAll versions (and the layout
/// docs/docs/modding/vortex.md documented) still build correctly.
/// </summary>
public static class FcbFragments
{
    private const uint EntityLibraryTypeHash = 0xBCDD10B4;
    private const uint EntityLibraryGroupTypeHash = 0xE0BDB3DB;
    private const uint WorldSectorTypeHash = 0xC1CB6D9A;
    private static readonly uint EntityPrototypeTypeHash = FcbClassDefinitions.Crc32Ascii("EntityPrototype");
    private static readonly uint EntityTypeHash = FcbClassDefinitions.Crc32Ascii("Entity");
    private static readonly uint MissionLayerTypeHash = FcbClassDefinitions.Crc32Ascii("MissionLayer");
    private static readonly uint NameFieldHash = FcbClassDefinitions.Crc32Ascii("Name");
    private static readonly uint HidNameFieldHash = FcbClassDefinitions.Crc32Ascii("hidName");
    private static readonly uint DisEntityIdFieldHash = FcbClassDefinitions.Crc32Ascii("disEntityId");
    private static readonly uint TextPathIdFieldHash = FcbClassDefinitions.Crc32Ascii("text_PathId");
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
        => [.. Slots(root).Select(s => new FcbFragment(s.Id, s.Parent.Children[s.Index]))];

    /// <summary>The node <paramref name="fragmentId"/> names — a deep fragment, or a whole library
    /// group via the legacy <c>NN_Name.xml</c> alias. A deep fragment wins when an id happens to
    /// match both spaces — <see cref="FcbAssembler.Apply"/> honours the same precedence. Null when
    /// neither matches.</summary>
    public static FcbObject? Find(FcbObject root, string fragmentId)
    {
        foreach (FragmentSlot slot in Slots(root))
        {
            if (IdComparer.Equals(slot.Id, fragmentId))
            {
                return slot.Parent.Children[slot.Index];
            }
        }

        return FindLegacyGroupIndex(root, fragmentId) is { } index ? root.Children[index] : null;
    }

    /// <summary>Where <see cref="FcbAssembler.Apply"/> attaches content whose fragment id matched
    /// nothing — new content a mod adds. A new node in a world sector joins the <c>main</c> mission
    /// layer (falling back to the first layer); in a library, a whole new <c>EntityLibraryGroup</c>
    /// keeps the pre-deep-fragment behaviour of joining the root, while anything else (a new
    /// archetype's prototype) joins the last group — appending a non-group at the root would stop the
    /// whole container from splitting. An unrecognised root appends at the root.</summary>
    internal static FcbObject AppendTarget(FcbObject root, FcbObject addition)
    {
        if (root.TypeHash == WorldSectorTypeHash)
        {
            FcbObject? first = null;
            foreach (FcbObject layer in root.Children)
            {
                if (layer.TypeHash != MissionLayerTypeHash)
                {
                    continue;
                }
                first ??= layer;
                if (ReadCString(layer, TextPathIdFieldHash).Equals("main", StringComparison.OrdinalIgnoreCase))
                {
                    return layer;
                }
            }
            return first ?? root;
        }

        if (addition.TypeHash != EntityLibraryGroupTypeHash && IsLibraryOfGroups(root))
        {
            return root.Children[^1];
        }

        return root;
    }

    /// <summary>The index of the library group the legacy <c>NN_Name.xml</c> id names, or null.</summary>
    private static int? FindLegacyGroupIndex(FcbObject root, string fragmentId)
    {
        if (!TryGetGroupIds(root, out IReadOnlyList<string> groupIds))
        {
            return null;
        }
        for (int i = 0; i < groupIds.Count; i++)
        {
            if (string.Equals(groupIds[i], fragmentId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return null;
    }

    /// <summary>
    /// The group-level <c>NN_Name.xml</c> id space: what <see cref="FcbXml.ToXml"/> names its
    /// Gibbed-compatible external files, and the alias older staged overrides resolve through — one id
    /// per <c>root.Children</c> entry, in order. False when <paramref name="root"/> isn't an entity
    /// library of groups.
    /// </summary>
    internal static bool TryGetGroupIds(FcbObject root, out IReadOnlyList<string> ids)
    {
        if (!IsLibraryOfGroups(root))
        {
            ids = [];
            return false;
        }

        var computed = new List<string>(root.Children.Count);
        int counter = 0;
        int padLength = root.Children.Count.ToString(CultureInfo.InvariantCulture).Length;
        foreach (FcbObject child in root.Children)
        {
            counter++;
            string fileBaseName = counter.ToString(CultureInfo.InvariantCulture).PadLeft(padLength, '0');
            string name = ReadCString(child, NameFieldHash);
            if (name.Length > 0)
            {
                fileBaseName += "_" + Sanitize(name);
            }
            computed.Add(fileBaseName + ".xml");
        }

        ids = computed;
        return true;
    }

    /// <summary>A fragment's place in the tree, held as parent + index so a replacement can be
    /// written straight back into the same slot.</summary>
    internal readonly record struct FragmentSlot(string Id, FcbObject Parent, int Index);

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
                    if (prototype.TypeHash != EntityPrototypeTypeHash)
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
        else if (root.TypeHash == WorldSectorTypeHash)
        {
            foreach (FcbObject layer in root.Children)
            {
                if (layer.TypeHash != MissionLayerTypeHash)
                {
                    continue;
                }
                for (int i = 0; i < layer.Children.Count; i++)
                {
                    FcbObject entity = layer.Children[i];
                    if (entity.TypeHash != EntityTypeHash
                        || !entity.Values.TryGetValue(DisEntityIdFieldHash, out byte[]? idBytes)
                        || idBytes.Length < 8)
                    {
                        continue;
                    }
                    ulong disEntityId = BitConverter.ToUInt64(idBytes, 0);
                    slots.Add(new FragmentSlot(EntityId(ReadCString(entity, HidNameFieldHash), disEntityId), layer, i));
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
            if (child.TypeHash == EntityTypeHash)
            {
                string name = ReadCString(child, HidNameFieldHash);
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
        && root.TypeHash == EntityLibraryTypeHash
        && root.Children.Count > 0
        && root.Children.All(c => c.TypeHash == EntityLibraryGroupTypeHash);

    private static string ReadCString(FcbObject node, uint fieldHash)
        => node.Values.TryGetValue(fieldHash, out byte[]? bytes) && bytes.Length > 0 && bytes[^1] == 0
            ? Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1)
            : "";

    private static string Sanitize(string name)
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
