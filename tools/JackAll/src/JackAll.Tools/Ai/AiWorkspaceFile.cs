using System.Xml.Linq;
using JackAll.Core.Format;
using JackAll.Core.Format.Rml;

namespace JackAll.Tools.Ai;

/// <summary>
/// A <c>scripts\game\newbrains\*.ai.rml</c> brain workspace: the compiled task repository the engine
/// runs, followed by the <c>BlackBox.AI</c> source it was compiled from, as an RML document.
/// </summary>
public sealed record AiWorkspaceFile(byte[] Packed, XElement Source)
{
    public const string Folder = @"scripts\game\newbrains\";

    /// <summary>The header kind the engine loads without recompiling the source.</summary>
    private const uint PackedKind = 4;

    public static bool IsWorkspace(string path)
        => path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".ai.rml", StringComparison.OrdinalIgnoreCase);

    public static AiWorkspaceFile Read(byte[] data)
    {
        var r = new ByteCursor(data);
        uint kind = r.ReadU32();
        int packed = (int)r.ReadU32();
        int source = (int)r.ReadU32();
        if (kind != PackedKind || r.Remaining != packed + source)
        {
            throw new InvalidDataException("Not a packed .ai.rml brain workspace.");
        }
        return new AiWorkspaceFile(r.ReadBytes(packed), RmlDocument.Deserialize(r.ReadBytes(source)));
    }

    /// <summary>A workspace for <paramref name="source"/>, with its compiled half built from it.</summary>
    public static byte[] Compile(XElement source) => new AiWorkspaceFile(AiWorkspacePacker.Pack(source), source).Write();

    public byte[] Write()
    {
        byte[] source = RmlDocument.Serialize(Source);
        var w = new ByteWriter();
        w.WriteU32(PackedKind);
        w.WriteU32((uint)Packed.Length);
        w.WriteU32((uint)source.Length);
        w.WriteRaw(Packed);
        w.WriteRaw(source);
        return w.ToArray();
    }
}
