using JackAll.Core.Format.Fcb;

namespace JackAll.Core.Mods;

/// <summary>
/// The `.fcb` container format as an <see cref="IContainerSplitter"/> - entity libraries split per
/// archetype, world sectors per placed entity.
/// </summary>
/// <remarks>
/// Nothing here is new behaviour; it is the wiring that used to be spelled out inline in
/// <c>PatchBuilder</c> and <c>GameVfs</c>, so a second format could exist alongside it.
/// </remarks>
public sealed class FcbContainerSplitter(FcbClassDefinitions definitions) : IContainerSplitter
{
    public IContainerTree Open(byte[] container) => Open(FcbDocument.Deserialize(container));

    /// <summary>The same tree over a root somebody has already decoded, so a caller holding one does
    /// not pay to deserialize the container a second time.</summary>
    public IContainerTree Open(FcbObject root) => new Tree(root, definitions);

    public string Canonicalize(string fragmentXml) => FcbXml.CanonicalizeFragment(fragmentXml, definitions);

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
        => FcbAssembler.Apply(baseBytes, fragmentXmlById);

    private sealed class Tree : IContainerTree
    {
        private readonly FcbObject _root;
        private readonly FcbClassDefinitions _definitions;
        private readonly IReadOnlyList<FcbFragment> _fragments;
        private readonly Dictionary<string, FcbObject> _byId;

        /// <summary>Indexed up front, for the same reason `depload` is: an import asks one container
        /// for every fragment it has, and <see cref="FcbFragments.Find"/> rebuilds the whole slot
        /// table per lookup - the shape that turns into O(n*m) over a 6 MB entity library.</summary>
        public Tree(FcbObject root, FcbClassDefinitions definitions)
        {
            _root = root;
            _definitions = definitions;
            _fragments = FcbFragments.List(root);
            _byId = new Dictionary<string, FcbObject>(_fragments.Count, FcbFragments.IdComparer);
            foreach (FcbFragment fragment in _fragments)
            {
                _byId[fragment.Id] = fragment.Node;
            }
        }

        public string? Extract(string fragmentId)
            => _byId.TryGetValue(fragmentId, out FcbObject? node) ? FcbXml.ToXml(node, _definitions) : null;

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _fragments.Select(f => new FcbFragmentInfo(f.Id, FcbDocument.EncodedSize(f.Node)))];

        public string? Skeleton(Func<string, bool> keep)
            => FcbXml.SkeletonXml(_root, _fragments, keep, _definitions);
    }
}
