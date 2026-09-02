using System.Globalization;
using System.Text;
using JackAll.Core.Format;
using JackAll.Core.Format.Fcb;

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
/// Fragments are keyed by the parent's CRC32, because that is the only id the binary itself carries -
/// see <see cref="IdOf"/> for the readable form that puts a name in front of it.
/// </remarks>
public sealed class DepLoadContainerSplitter : IContainerSplitter
{
    /// <summary>Stateless, so every dispatch can share one.</summary>
    public static DepLoadContainerSplitter Instance { get; } = new();

    /// <summary>
    /// The fragment id a parent is staged under: its CRC32 in decimal, optionally behind a name to
    /// read by. <c>dragunov.3882209901.xml</c> and a bare <c>3882209901.xml</c> are the same
    /// fragment.
    /// </summary>
    /// <remarks>
    /// The name is cosmetic and the number authoritative, which is the scheme a placed entity's
    /// fragment already uses (<c>Guard_12.2058514756624450165.xml</c>) - so
    /// <see cref="FcbFragments.IdComparer"/> collapses the prefix with no special-casing, and
    /// renaming a staged file cannot orphan the override. Decimal rather than hex precisely because
    /// that comparer keys on a *numeric* tail; it is also how the shipped `_depload.xml` twins print
    /// <c>crc_ID</c>. The binary stores no name at all, so one is only ever supplied by whoever
    /// already knows it - nothing has to resolve a hash back to a name.
    /// </remarks>
    public static string IdOf(uint parentHash, string? name = null)
    {
        string leaf = Sanitize(name);
        return leaf.Length == 0
            ? $"{parentHash}.xml"
            : $"{leaf}.{parentHash}.xml";
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

    public IContainerTree Open(byte[] container) => new Tree(DepLoadDocument.Decode(container));

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
                    + $"Name it '{IdOf(parent.Hash)}' (any label ahead of the number is yours to "
                    + "choose), or fix the resource it names.");
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
    /// The CRC32 a fragment id names, or null when it is not one of this format's ids. Reads the same
    /// numeric tail <see cref="FcbFragments.IdComparer"/> keys on, so any two ids that comparer calls
    /// equal resolve to one parent here too - there is no second notion of "the same fragment".
    /// </summary>
    private static uint? HashOf(string fragmentId)
    {
        if (!fragmentId.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ReadOnlySpan<char> stem = fragmentId.AsSpan(0, fragmentId.Length - 4);
        ReadOnlySpan<char> tail = stem[(stem.LastIndexOf('.') + 1)..];
        return uint.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out uint hash)
            ? hash
            : null;
    }

    private sealed class Tree : IContainerTree
    {
        private readonly Dictionary<uint, DepLoadParent> _byHash;

        /// <summary>Indexed up front: a build asks one container for many fragments, and a linear
        /// scan of ~9,800 parents per lookup is the shape that turns into O(n*m).</summary>
        public Tree(DepLoadFile file) => _byHash = file.Parents.ToDictionary(p => p.Hash);

        public string? Extract(string fragmentId)
            => HashOf(fragmentId) is { } hash && _byHash.TryGetValue(hash, out DepLoadParent? parent)
                ? DepLoadXml.FragmentToXml(parent)
                : null;
    }
}
