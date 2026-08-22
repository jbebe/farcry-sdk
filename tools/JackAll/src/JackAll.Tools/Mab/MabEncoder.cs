using System.Buffers.Binary;

namespace JackAll.Tools.Mab;

/// <summary>
/// Builds a clip's sections from decoded tracks - the inverse of the readers on
/// <see cref="MabClip"/>.
/// </summary>
/// <remarks>
/// Layout rules are documented in docs/docs/file-formats/mab.md. The ones that bite: a section
/// starts on a 16-byte boundary and pads with <b>zeros</b>, not the descending counter an `.xbg`
/// uses, and the sections appear in the order 1, 0, 2, 3, 4, 5, 6, 7, 8 rather than by slot.
/// </remarks>
public static class MabEncoder
{
    /// <summary>Sections are 16-byte aligned and zero-padded.</summary>
    public const int Alignment = 16;

    /// <summary>An array section's header: track count, last frame, rate, and a zero.</summary>
    public static void WriteTrackHeader(Span<byte> destination, int tracks, int lastFrame, int rate)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)tracks);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)lastFrame);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)rate);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[6..], 0);
    }

    /// <summary>One rotation per bone, held for the whole clip.</summary>
    public static byte[] ConstantRotations(IReadOnlyList<int> bones, IReadOnlyDictionary<int, float[]> values)
    {
        var section = new byte[MabClip.TrackHeader + (bones.Count * MabClip.QuatBytes)];
        WriteTrackHeader(section, bones.Count, 0, 0);
        for (int slot = 0; slot < bones.Count; slot++)
        {
            MabClip.WriteQuaternion(
                section.AsSpan(MabClip.TrackHeader + (slot * MabClip.QuatBytes)),
                Value(values, bones[slot], "constant rotation"));
        }
        return section;
    }

    /// <summary>One offset per bone, held for the whole clip.</summary>
    public static byte[] ConstantTranslations(IReadOnlyList<int> bones, IReadOnlyDictionary<int, float[]> values)
    {
        var section = new byte[MabClip.TrackHeader + (bones.Count * MabClip.Vec3Bytes)];
        WriteTrackHeader(section, bones.Count, 0, 0);
        for (int slot = 0; slot < bones.Count; slot++)
        {
            WriteVec3(
                section.AsSpan(MabClip.TrackHeader + (slot * MabClip.Vec3Bytes)),
                Value(values, bones[slot], "constant translation"));
        }
        return section;
    }

    /// <summary>A frame-major section: every track's value at frame 0, then at 1, and so on.</summary>
    public static byte[] DenseTranslations(
        IReadOnlyList<int> bones,
        IReadOnlyDictionary<int, List<(int Frame, float[]? Value)>> tracks,
        int lastFrame,
        int rate)
    {
        int frames = lastFrame + 1;
        var section = new byte[MabClip.TrackHeader + (frames * bones.Count * MabClip.Vec3Bytes)];
        WriteTrackHeader(section, bones.Count, lastFrame, rate);
        for (int frame = 0; frame < frames; frame++)
        {
            int at = MabClip.TrackHeader + (frame * bones.Count * MabClip.Vec3Bytes);
            for (int slot = 0; slot < bones.Count; slot++)
            {
                WriteVec3(section.AsSpan(at + (slot * MabClip.Vec3Bytes)), tracks[bones[slot]][frame].Value!);
            }
        }
        return section;
    }

    /// <summary>The same, for a trajectory's single rotation track.</summary>
    public static byte[] DenseRotations(
        IReadOnlyList<(int Frame, float[]? Value)> track, int lastFrame, int rate)
    {
        int frames = lastFrame + 1;
        var section = new byte[MabClip.TrackHeader + (frames * MabClip.QuatBytes)];
        WriteTrackHeader(section, 1, lastFrame, rate);
        for (int frame = 0; frame < frames; frame++)
        {
            MabClip.WriteQuaternion(
                section.AsSpan(MabClip.TrackHeader + (frame * MabClip.QuatBytes)), track[frame].Value!);
        }
        return section;
    }

    /// <summary>
    /// The sparse rotation block: frames in groups of eight, each group holding every track's
    /// rotation at its first frame, then a presence byte per track, then the keys those bytes name.
    /// </summary>
    /// <remarks>
    /// A group's own first frame is always stored and never appears in a presence byte - bit i means
    /// a key at subframe i + 1. The presence run is padded to an even length, and the group offsets
    /// are measured from the start of the section.
    /// </remarks>
    public static byte[] KeyframeRotations(
        IReadOnlyList<int> bones,
        IReadOnlyDictionary<int, List<(int Frame, float[]? Rotation)>> tracks,
        int lastFrame,
        int rate)
    {
        int groups = (lastFrame >> MabClip.GroupShift) + 1;
        int presenceBytes = (bones.Count + 1) & ~1;

        // Which subframes each track keys, per group, so the size is known before writing.
        var present = new byte[groups, bones.Count];
        var keys = new Dictionary<(int Group, int Slot, int Bit), float[]>();
        for (int slot = 0; slot < bones.Count; slot++)
        {
            foreach ((int frame, float[]? rotation) in tracks[bones[slot]])
            {
                int group = frame >> MabClip.GroupShift;
                int bit = (frame & (MabClip.GroupFrames - 1)) - 1;
                if (bit < 0 || rotation is null)
                {
                    continue;
                }
                present[group, slot] |= (byte)(1 << bit);
                keys[(group, slot, bit)] = rotation;
            }
        }

        var body = new List<byte>();
        // One offset per group and a final one for where the last group ends, so a reader can size
        // any group by subtracting - which is also why the table is groups + 1 long.
        var offsets = new int[groups + 1];
        int headerSize = MabClip.TrackHeader + ((groups + 1) * 4);
        for (int group = 0; group < groups; group++)
        {
            offsets[group] = headerSize + body.Count;
            int first = group << MabClip.GroupShift;

            var block = new byte[(bones.Count * MabClip.QuatBytes) + presenceBytes];
            for (int slot = 0; slot < bones.Count; slot++)
            {
                MabClip.WriteQuaternion(
                    block.AsSpan(slot * MabClip.QuatBytes), At(tracks[bones[slot]], first));
                block[(bones.Count * MabClip.QuatBytes) + slot] = present[group, slot];
            }
            body.AddRange(block);

            for (int slot = 0; slot < bones.Count; slot++)
            {
                for (int bit = 0; bit < MabClip.GroupFrames - 1; bit++)
                {
                    if (keys.TryGetValue((group, slot, bit), out float[]? rotation))
                    {
                        var packed = new byte[MabClip.QuatBytes];
                        MabClip.WriteQuaternion(packed, rotation);
                        body.AddRange(packed);
                    }
                }
            }
        }

        offsets[groups] = headerSize + body.Count;

        var section = new byte[headerSize + body.Count];
        WriteTrackHeader(section, bones.Count, lastFrame, rate);
        for (int group = 0; group <= groups; group++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                section.AsSpan(MabClip.TrackHeader + (group * 4)), offsets[group]);
        }
        body.CopyTo(section, headerSize);
        return section;
    }

    /// <summary>The bytes a section occupies once padded to the alignment.</summary>
    public static int Padded(int length) => (length + Alignment - 1) & ~(Alignment - 1);

    /// <summary>
    /// One masked bone's value, or a refusal naming what is missing.
    /// </summary>
    /// <remarks>
    /// A decode can come back short of its mask two ways: the section's own count can be lower than
    /// the mask's popcount, and 126 stored triples across the shipped set are not unit rotations at
    /// all, so they decode to nothing. Neither can be written back, and saying so is better than
    /// inventing a value.
    /// </remarks>
    private static float[] Value(IReadOnlyDictionary<int, float[]> values, int bone, string what)
        => values.TryGetValue(bone, out float[]? value)
            ? value
            : throw new InvalidDataException($"The {what} mask names bone {bone}, which decoded to nothing.");

    private static float[] At(List<(int Frame, float[]? Rotation)> track, int frame)
        => track.FirstOrDefault(key => key.Frame == frame).Rotation
           ?? throw new InvalidDataException($"No rotation at frame {frame}, which a group must hold.");

    private static void WriteVec3(Span<byte> destination, float[] value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, value[0]);
        BinaryPrimitives.WriteSingleLittleEndian(destination[4..], value[1]);
        BinaryPrimitives.WriteSingleLittleEndian(destination[8..], value[2]);
    }
}
