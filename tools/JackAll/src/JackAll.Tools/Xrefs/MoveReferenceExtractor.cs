using JackAll.Core.Vfs;
using JackAll.Core.Xrefs;
using JackAll.Core.Format.Move;

namespace JackAll.Tools.Xrefs;

/// <summary>
/// References inside a MOVE graph (`movemgr.bin`, `dlc1.bin`): every `.mab` clip it plays.
/// </summary>
/// <remarks>
/// A clip reference is already the path hash of its `.mab`, so it lands directly in
/// <see cref="RefSpace.FilePath"/>. The `*named.bin` authoring twins are deliberately not claimed:
/// the engine never reads them.
/// </remarks>
public sealed class MoveReferenceExtractor : IReferenceExtractor
{
    public bool CanHandle(VfsFile file) => file.FileName is "movemgr.bin" or "dlc1.bin";

    public void Extract(VfsFile file, byte[] content, ReferenceSink sink)
    {
        MoveFile move = MoveCodec.Load(content);
        uint site = sink.Intern("m_animNameHash");

        foreach (uint clip in MoveWeapons.AllClipReferences(move).Keys)
        {
            sink.Add(RefSpace.FilePath, clip, RefKind.MoveClip, site);
        }
    }
}
