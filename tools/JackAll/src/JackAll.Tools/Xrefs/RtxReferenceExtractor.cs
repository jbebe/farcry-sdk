using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Tools.Rtx;

namespace JackAll.Tools.Xrefs;

/// <summary>
/// References inside an `.rtx` species: the material bound to each slot, rewritten from the
/// authoring `.mlm` path to the `.xbm` of the same stem that actually ships.
/// </summary>
public sealed class RtxReferenceExtractor : IReferenceExtractor
{
    private static readonly string[] SlotNames = ["bark", "leafcards", "hybridleaves"];

    public bool CanHandle(VfsFile file) => file.Type.Extension is "rtx";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        RtxModel model = RtxModel.Parse(content);

        for (int slot = 0; slot < model.Materials.Count; slot++)
        {
            if (model.Materials[slot] is not { Length: > 0 } material)
            {
                continue;
            }

            string site = slot < SlotNames.Length ? SlotNames[slot] : "material";
            sink.AddNamedPath(Path.ChangeExtension(material, ".xbm"), RefKind.RtxMaterial, site, slot);
        }
    }
}
