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
/// References inside an `.xbg` mesh: the material each submesh is drawn with.
/// </summary>
/// <remarks>
/// A mesh names its materials rather than pointing at a file, so these land in
/// <see cref="RefSpace.EngineName"/>, not <see cref="RefSpace.FilePath"/> - the same name an `.xbm`
/// defines. Following one to an actual material file therefore depends on the `.xbm` side being
/// indexed too, which is why the two extractors are worth having as a pair even though the mesh half
/// looks thin on its own.
/// </remarks>
public sealed class XbgReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.Type.Extension is "xbg";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        XbgModel model = XbgModel.Parse(content);
        uint site = sink.Intern("material");

        for (int i = 0; i < model.Materials.Count; i++)
        {
            string name = model.Materials[i];
            if (name.Length == 0)
            {
                continue;
            }

            // The mesh spells the name out, so it can be interned as its own site vocabulary entry -
            // that's what lets an unresolved material hash still render as a readable name later.
            sink.Add(RefSpace.EngineName, sink.Intern(name), RefKind.XbgMaterial, site, i);
        }
    }
}
