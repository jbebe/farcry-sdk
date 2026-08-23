using System.Buffers.Binary;

namespace JackAll.Tools.Mab;

/// <summary>
/// One clip's own content: the fields its fixed header carries, and its sections bar the chained
/// clip, which the bank assembler supplies.
/// </summary>
public sealed class MabClipParts
{
    public required uint[][] Masks { get; init; }

    public required float[] ReferenceRotation { get; init; }

    public required float[] LoopRotation { get; init; }

    public required float Duration { get; init; }

    public required IReadOnlyDictionary<int, byte[]> Sections { get; init; }

    /// <summary>Everything this clip carried but its chained clip and its section offsets.</summary>
    public static MabClipParts Of(MabClip clip, IReadOnlyDictionary<int, byte[]> sections) => new()
    {
        Masks = [.. clip.Masks.Select(mask => (uint[])[.. mask])],
        ReferenceRotation = [.. clip.ReferenceRotation],
        LoopRotation = [.. clip.LoopRotation],
        Duration = clip.Duration,
        Sections = sections.Where(pair => pair.Key != MabClip.SectionNextClip)
            .ToDictionary(pair => pair.Key, pair => pair.Value),
    };

    /// <summary>The clip's fixed header, with the offsets a layout produced.</summary>
    public byte[] Header(int[] sectionOffsets)
    {
        var header = new byte[MabClip.ClipHeader];
        int at = 0;
        foreach (uint[] mask in Masks)
        {
            foreach (uint word in mask)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(at), word);
                at += 4;
            }
        }
        foreach (float value in ReferenceRotation.Concat(LoopRotation))
        {
            BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(at), value);
            at += 4;
        }

        MabClip.BodyTag.CopyTo(header.AsSpan(at));
        BinaryPrimitives.WriteSingleLittleEndian(header.AsSpan(at + 4), Duration);
        at += 8;
        foreach (int offset in sectionOffsets)
        {
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(at), offset);
            at += 4;
        }
        // The slot the engine parks its own pointer in stays zero on disk.
        return header;
    }
}
