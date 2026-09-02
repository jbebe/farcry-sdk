using JackAll.Core.Format;
using JackAll.Core.Mods;
using JackAll.Core.Vfs;

namespace JackAll.Core.Xrefs;

/// <summary>
/// References inside a `depload.dat` - the per-world dependency-preload index.
/// </summary>
/// <remarks>
/// This is the one format that is *entirely* references (see <see cref="DepLoadDocument"/>: "not a
/// container of embedded file bytes ... every entry is a CRC32 reference to another resource"), and
/// the one the app already lets you follow one link at a time via <c>DependencyLinkHandler</c>. What
/// it never answered until now is the other direction - "which worlds preload this texture?" - which
/// falls straight out of indexing the same edges.
///
/// A parent's own hash becomes the site key of each of its dependency edges, so an xref row can say
/// which parent pulled the target in without a second lookup table. The child's type tag is emitted
/// as its own edge into <see cref="RefSpace.DepLoadType"/>: the type hash's meaning isn't confirmed,
/// but grouping every dependency sharing one is useful regardless (a real file has 1,314 children
/// across just 8 distinct types).
/// </remarks>
public sealed class DepLoadReferenceExtractor : IReferenceExtractor
{
    /// <summary>
    /// Matched by filename suffix, not bare extension - "dat" alone is also the archive-container
    /// extension, the same distinction <c>FileHandlerCatalog</c> makes for this format's viewer.
    /// </summary>
    public bool CanHandle(VfsFile file)
        => file.Type.Extension is "dat"
        && ContainerFormats.IsDepLoad(file.FileName);

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        DepLoadFile depload = DepLoadDocument.Decode(content);

        foreach (DepLoadParent parent in depload.Parents)
        {
            for (int i = 0; i < parent.Children.Count; i++)
            {
                DepLoadChild child = parent.Children[i];
                sink.Add(RefSpace.FilePath, child.Hash, RefKind.DepLoadDependency, parent.Hash, i);
                sink.Add(RefSpace.DepLoadType, child.TypeHash, RefKind.DepLoadTypeTag, child.Hash);
            }
        }
    }
}
