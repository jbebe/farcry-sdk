using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Xbt;

namespace JackAll.Tools.Xrefs;

/// <summary>
/// References inside an `.xbt` texture: the `_mip0.xbt` streaming companion its header names.
/// </summary>
/// <remarks>
/// The companion holds the texture's real top mip level and is named nowhere else - no manifest or
/// material ever spells its path - so without this edge every one of those files looks unreferenced.
/// </remarks>
public sealed class XbtReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.Type.Extension is "xbt";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        var (header, _) = XbtTexture.Split(content);
        sink.AddNamedPath(XbtTexture.CompanionPath(header), RefKind.XbtMipCompanion, "EmbeddedPath");
    }
}
