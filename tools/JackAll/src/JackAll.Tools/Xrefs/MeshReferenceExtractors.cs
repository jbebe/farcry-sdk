using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Xbg;
using JackAll.Tools.Xbm;

namespace JackAll.Tools.Xrefs;

/// <summary>
/// References inside an `.xbm` material: the `.xbt` bound to each texture slot.
/// </summary>
/// <remarks>
/// This is the edge the whole feature is most obviously *for* - "which materials use this texture?"
/// is the first question anyone retexturing something asks, and until now the only way to answer it
/// was to open materials one at a time. The slot key ("DiffuseTexture1") becomes the site, so the
/// xref row says not just which material but which slot of it.
/// </remarks>
public sealed class XbmReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.Type.Extension is "xbm";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        XbmMaterial material = XbmMaterial.Parse(content);

        foreach (XbmProperty texture in material.Textures)
        {
            sink.AddNamedPath(texture.Value, RefKind.XbmTexture, texture.Key);
        }
    }
}

/// <summary>
/// References inside an `.xbg` mesh: the `.xbm` each submesh is drawn with, named by its path.
/// </summary>
public sealed class XbgReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.Type.Extension is "xbg";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        XbgModel model = XbgModel.Parse(content);
        for (int i = 0; i < model.Materials.Count; i++)
        {
            sink.AddNamedPath(model.Materials[i], RefKind.XbgMaterial, "material", i);
        }
    }
}
