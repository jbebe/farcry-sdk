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

    /// <summary>The fragment id a resource is staged under - see <see cref="FragmentId"/>. The
    /// binary stores only a CRC32, so that is the number.</summary>
    public static string IdOf(uint parentHash, string? name = null) => FragmentId.Of(parentHash, name);

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
            if (FragmentId.NumberOf(id) != parent.Hash)
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
    public static uint? ResourceOf(string fragmentId) => FragmentId.NumberOf(fragmentId);

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
            => FragmentId.NumberOf(fragmentId) is { } hash && _byHash.TryGetValue(hash, out DepLoadParent? parent)
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

        /// <summary>
        /// The kept parents' ids in file order. A `depload.dat` is nothing but its parents, so that
        /// order is the whole of its shape - a parent's own <c>ChildIndex</c> is block layout that
        /// <see cref="Apply"/> takes from the base file, never from a fragment.
        /// </summary>
        public string? Skeleton(Func<string, bool> keep)
            => string.Join('\n', _file.Parents.Select(p => IdOf(p.Hash)).Where(keep));

        /// <summary>A resource's own path, but only one that hashes back to it - the id has to stay
        /// resolvable without a hashlist, so a name that disagrees is no use as an address.</summary>
        private string? NameFor(uint hash)
            => _names is not null && _names.TryResolve(hash, out string path) ? path : null;
    }
}
