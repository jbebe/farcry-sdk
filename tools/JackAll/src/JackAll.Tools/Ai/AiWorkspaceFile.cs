using System.Buffers.Binary;
using System.Xml.Linq;
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

    public static AiWorkspaceFile Read(byte[] data)
    {
        uint kind = BinaryPrimitives.ReadUInt32LittleEndian(data);
        int packed = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        int source = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8));
        if (kind != PackedKind || 12 + packed + source != data.Length)
        {
            throw new InvalidDataException("Not a packed .ai.rml brain workspace.");
        }
        return new AiWorkspaceFile(
            data.AsSpan(12, packed).ToArray(),
            RmlDocument.Deserialize(data.AsSpan(12 + packed, source).ToArray()));
    }

    public byte[] Write()
    {
        byte[] source = RmlDocument.Serialize(Source);
        byte[] data = new byte[12 + Packed.Length + source.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, PackedKind);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)Packed.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)source.Length);
        Packed.CopyTo(data, 12);
        source.CopyTo(data, 12 + Packed.Length);
        return data;
    }
}
