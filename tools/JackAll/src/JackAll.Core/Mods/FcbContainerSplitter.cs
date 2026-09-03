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

    public string Canonicalize(string fragmentId, string fragmentXml)
        => WorldSectorLayout.IsLayoutId(fragmentId)
            ? WorldSectorLayout.Parse(fragmentXml).Render()
            : FcbXml.CanonicalizeFragment(fragmentXml, definitions);

    /// <summary>
    /// A mission-layer layout is merged by what it means - which entity belongs to which layer -
    /// rather than as text, because two mods moving different entities touch neighbouring lines of
    /// the same document and a line-based merge would call that a conflict.
    /// </summary>
    public (string Merged, bool Conflict) Merge(string fragmentId, string ancestor, string ours, string theirs)
    {
        if (!WorldSectorLayout.IsLayoutId(fragmentId))
        {
            return IContainerSplitter.TextMerge(ancestor, ours, theirs);
        }

        (WorldSectorLayout merged, bool conflict) = WorldSectorLayout.Merge(
            ParseLayout(ancestor), ParseLayout(ours), ParseLayout(theirs));
        return (merged.Render(), conflict);
    }

    /// <summary>An absent ancestor is a sector nobody has re-filed yet, which is an empty layout
    /// rather than an error - the same way a missing fragment means new content.</summary>
    private static WorldSectorLayout ParseLayout(string xml)
        => xml.Length == 0 ? new WorldSectorLayout([]) : WorldSectorLayout.Parse(xml);

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
        => FcbAssembler.Apply(baseBytes, fragmentXmlById);

    /// <summary>One entity cannot be both deleted by a sector's layout and overridden as a fragment
    /// beside it.</summary>
    public IReadOnlyList<(string FragmentId, string Kept, string Overruled)> Contradictions(
        IReadOnlyDictionary<string, string> resolved)
    {
        if (!resolved.TryGetValue(WorldSectorLayout.Id, out string? layoutXml))
        {
            return [];
        }

        return [.. WorldSectorLayout.Parse(layoutXml).Contested(resolved.Keys)
            .Select(id => (FcbFragments.EntityFragmentId(id), "the mod that edits it", "a mod that deletes it"))];
    }

    private sealed class Tree : IContainerTree
    {
        private readonly FcbObject _root;
        private readonly FcbClassDefinitions _definitions;
        private readonly IReadOnlyList<FcbFragment> _fragments;
        private readonly Dictionary<string, (FcbObject Node, FcbObject Parent)> _byId;

        /// <summary>Indexed up front, for the same reason `depload` is: an import asks one container
        /// for every fragment it has, and <see cref="FcbFragments.Find"/> rebuilds the whole slot
        /// table per lookup - the shape that turns into O(n*m) over a 6 MB entity library.</summary>
        public Tree(FcbObject root, FcbClassDefinitions definitions)
        {
            _root = root;
            _definitions = definitions;
            List<FcbFragments.FragmentSlot> slots = FcbFragments.Slots(root);
            _fragments = [.. slots.Select(s => new FcbFragment(s.Id, s.Node))];
            _byId = new Dictionary<string, (FcbObject, FcbObject)>(slots.Count, FcbFragments.IdComparer);
            foreach (FcbFragments.FragmentSlot slot in slots)
            {
                _byId[slot.Id] = (slot.Node, slot.Parent);
            }
        }

        /// <summary>
        /// The layout id answers with the container's whole current placement, not just the part a
        /// mod changed - it is the ancestor a staged layout is merged against, and the base an editor
        /// diffs. Every other id is an ordinary fragment.
        /// </summary>
        public string? Extract(string fragmentId)
        {
            if (WorldSectorLayout.IsLayoutId(fragmentId))
            {
                return _root.TypeHash == WorldHashes.WorldSector ? WorldSectorLayout.Of(_root).Render() : null;
            }

            return _byId.TryGetValue(fragmentId, out (FcbObject Node, FcbObject Parent) found)
                ? FcbXml.ToXml(found.Node, _definitions)
                : null;
        }

        /// <summary>A world sector's mission-layer placement, diffed straight off the two decoded
        /// roots rather than through the rendered form <see cref="Extract"/> hands out.</summary>
        public (string Id, string Xml)? StructuralOverride(IContainerTree ancestor)
        {
            if (ancestor is not Tree before || _root.TypeHash != WorldHashes.WorldSector)
            {
                return null;
            }

            return WorldSectorLayout.Diff(WorldSectorLayout.Of(before._root), WorldSectorLayout.Of(_root))
                is { } diff
                ? (WorldSectorLayout.Id, diff.Render())
                : null;
        }

        public FragmentAncestry? AncestryOf(string fragmentId)
        {
            if (!_byId.TryGetValue(fragmentId, out (FcbObject Node, FcbObject Parent) found))
            {
                return null;
            }

            return found.Parent.TypeHash == WorldHashes.MissionLayer
                ? new FragmentAncestry(
                    FragmentParentKind.MissionLayer,
                    MissionLayers.NameOf(found.Parent),
                    MissionLayers.PathIdOf(found.Parent),
                    MissionLayers.DeclaredLayerOf(found.Node))
                : new FragmentAncestry(
                    FragmentParentKind.LibraryGroup,
                    FcbEntityFields.ReadString(found.Parent, WorldHashes.Name),
                    null,
                    null);
        }

        public IReadOnlyList<FcbFragmentInfo> List()
            => [.. _fragments.Select(f => new FcbFragmentInfo(f.Id, FcbDocument.EncodedSize(f.Node)))];

        public string? Skeleton(Func<string, bool> keep)
            => FcbXml.SkeletonXml(_root, _fragments, keep, _definitions);
    }
}
