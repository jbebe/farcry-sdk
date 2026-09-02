using System.Globalization;
using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;
using JackAll.Core.Naming;

namespace JackAll.Core.Mods;

/// <summary>
/// A `depload.dat` as an <see cref="IContainerSplitter"/>: one fragment per parent, holding that
/// resource's whole dependency list.
/// </summary>
/// <remarks>
/// This is what lets a mod declare content at a path the game never shipped without hand-shipping a
/// 220 KB manifest - the registration an animation clip needs before the engine will load it. See
/// docs/docs/file-formats/depload.md.
///
/// A fragment is staged as <c>&lt;label&gt;.&lt;crc32&gt;.xml</c> - see <see cref="IdOf"/>. The binary
/// stores only a CRC32, so the number is what binds and the label is there to read by.
/// </remarks>
public sealed class DepLoadContainerSplitter(NameDatabase? names = null) : IContainerSplitter
{
    /// <summary>
    /// The nameless one, for a caller with no hashlist to hand. It lists fragments under their bare
    /// number, which compares equal to any labelled form, so a build never needs names.
    /// </summary>
    public static DepLoadContainerSplitter Instance { get; } = new();

    /// <summary>
    /// The fragment id a resource is staged under: <c>&lt;label&gt;.&lt;crc32 decimal&gt;.xml</c>,
    /// e.g. <c>dragunov.3882209901.xml</c>, or a bare <c>3882209901.xml</c> when there is no name to
    /// read it by.
    /// </summary>
    /// <remarks>
    /// The number binds and the label is decoration - the same cosmetic-name / authoritative-number
    /// shape a placed entity's fragment uses (<c>Guard_12.2058514756624450165.xml</c>). That is what
    /// makes every spelling of one resource compare equal under
    /// <see cref="FcbFragments.IdComparer"/> with no special case: whoever writes the id may know a
    /// name the reader does not, and vice versa, and they still land on one entry. Decimal precisely
    /// because that comparer keys on a *numeric* tail.
    ///
    /// The label has to be a flat leaf. <see cref="FcbFragments.Canonicalize"/> strips a cosmetic
    /// prefix only from the last path segment and keeps the directory, so a label spelled as a nested
    /// path (<c>graphics\weapons\…\dragunov.xbg.3882209901.xml</c>) would canonicalize to
    /// <c>graphics\weapons\…\3882209901.xml</c> and stop matching the bare form - which is exactly
    /// the mismatch this scheme exists to avoid.
    /// </remarks>
    public static string IdOf(uint parentHash, string? name = null)
    {
        string label = Sanitize(name);
        return label.Length == 0 ? $"{parentHash}.xml" : $"{label}.{parentHash}.xml";
    }

    /// <summary>The label part of an id: a bare filename, with anything a path or a filesystem would
    /// object to reduced to an underscore. Empty when there is nothing usable to read by.</summary>
    private static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        ReadOnlySpan<char> leaf = name.AsSpan(name.AsSpan().LastIndexOfAny('\\', '/') + 1).Trim();
        var text = new StringBuilder(leaf.Length);
        foreach (char c in leaf)
        {
            text.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        }
        return text.ToString();
    }

    public IContainerTree Open(byte[] container) => new Tree(DepLoadDocument.Decode(container), names);

    public string Canonicalize(string fragmentXml)
        => DepLoadXml.FragmentToXml(DepLoadXml.FragmentFromXml(fragmentXml));

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
    {
        if (fragmentXmlById.Count == 0)
        {
            return baseBytes;
        }

        DepLoadFile file = DepLoadDocument.Decode(baseBytes);
        var staged = new Dictionary<uint, DepLoadParent>();
        foreach ((string id, string xml) in fragmentXmlById)
        {
            DepLoadParent parent = DepLoadXml.FragmentFromXml(xml);
            if (HashOf(id) != parent.Hash)
            {
                throw new InvalidDataException(
                    $"A depload fragment staged as '{id}' describes resource {parent.Hash} instead. "
                    + $"Name it '{IdOf(parent.Hash)}' - any label ahead of the number is yours to "
                    + "choose - or fix the resource it names.");
            }
            staged[parent.Hash] = parent;
        }

        // An overridden parent keeps the block order it already had; a new one is appended, which is
        // what DepLoadEdit does for the same case.
        var parents = new List<DepLoadParent>(file.Parents.Count + staged.Count);
        foreach (DepLoadParent parent in file.Parents)
        {
            parents.Add(staged.Remove(parent.Hash, out DepLoadParent? replacement)
                ? replacement with { ChildIndex = parent.ChildIndex }
                : parent);
        }

        if (staged.Count > 0)
        {
            int next = DepLoadEdit.EndOfBlocks(parents);
            foreach (DepLoadParent addition in staged.Values.OrderBy(p => p.Hash))
            {
                parents.Add(addition with { ChildIndex = next++ });
            }
        }

        return DepLoadDocument.Encode(new DepLoadFile(parents));
    }

    /// <summary>
    /// The resource CRC32 a fragment id names, or null when it names nothing this format can address.
    /// The id is read through the same canonicalization <see cref="FcbFragments.IdComparer"/> keys
    /// on, so two ids that comparer calls equal resolve to one resource here too - there is no second
    /// notion of "the same fragment".
    /// </summary>
    /// <remarks>
    /// Public because a fragment row *is* a resource: anything asking what a row depends on, or what
    /// depends on it, needs the hash behind its id rather than the id's own text.
    /// </remarks>
    public static uint? ResourceOf(string fragmentId) => HashOf(fragmentId);

    private static uint? HashOf(string fragmentId)
    {
        if (!fragmentId.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string canonical = FcbFragments.Canonicalize(fragmentId);
        string stem = canonical[..^".xml".Length];
        if (stem.Length == 0)
        {
            return null;
        }

        // Canonicalization has already reduced a labelled id to its number. Anything left that is not
        // numeric is a hand-written id naming the resource outright, which still builds correctly -
        // it just cannot compare equal to the labelled form, so tooling never writes one.
        return uint.TryParse(stem, NumberStyles.None, CultureInfo.InvariantCulture, out uint hash)
            ? hash
            : NameHash.Compute(stem);
    }

    private sealed class Tree : IContainerTree
    {
        private readonly DepLoadFile _file;
        private readonly NameDatabase? _names;
        private readonly Dictionary<uint, DepLoadParent> _byHash;

        /// <summary>Indexed up front: a build asks one container for many fragments, and a linear
        /// scan of ~9,800 parents per lookup is the shape that turns into O(n*m).</summary>
        public Tree(DepLoadFile file, NameDatabase? names)
        {
            _file = file;
            _names = names;
            _byHash = file.Parents.ToDictionary(p => p.Hash);
        }

        public string? Extract(string fragmentId)
            => HashOf(fragmentId) is { } hash && _byHash.TryGetValue(hash, out DepLoadParent? parent)
                ? DepLoadXml.FragmentToXml(parent)
                : null;

        /// <summary>
        /// One entry per resource. The size is the fragment's own footprint in the binary - its
        /// 8-byte parent entry plus 5 bytes per dependency - which is O(1) per row; rendering each
        /// one's XML just to measure it would build several megabytes of text nobody reads.
        /// </summary>
        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _file.Parents.Select(p => new FcbFragmentInfo(
                IdOf(p.Hash, NameFor(p.Hash)), 8 + (5L * p.Children.Count)))];

        /// <summary>A resource's own path, but only one that hashes back to it - the id has to stay
        /// resolvable without a hashlist, so a name that disagrees is no use as an address.</summary>
        private string? NameFor(uint hash)
            => _names is not null && _names.TryResolve(hash, out string path) ? path : null;
    }
}
