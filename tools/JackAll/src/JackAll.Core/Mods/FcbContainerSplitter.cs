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
    public IContainerTree Open(byte[] container) => new Tree(FcbDocument.Deserialize(container), definitions);

    public string Canonicalize(string fragmentXml) => FcbXml.CanonicalizeFragment(fragmentXml, definitions);

    public byte[] Apply(byte[] baseBytes, IReadOnlyDictionary<string, string> fragmentXmlById)
        => FcbAssembler.Apply(baseBytes, fragmentXmlById);

    /// <summary>Every fragment of a container, with the size to show against it. Only `.fcb` files
    /// get browsable fragment rows, which is why this is here rather than on the interface.</summary>
    public IReadOnlyList<FcbFragmentInfo> ListFragments(byte[] container)
        => FcbXml.ListFragmentsWithSize(FcbDocument.Deserialize(container));

    private sealed class Tree(FcbObject root, FcbClassDefinitions definitions) : IContainerTree
    {
        public string? Extract(string fragmentId) => FcbXml.ExtractFragment(root, fragmentId, definitions);
    }
}
