using System.Buffers.Binary;
using JackAll.Core.Format;

namespace JackAll.Tools.Mab;

/// <summary>
/// Something a clip animates besides its own skeleton.
/// </summary>
/// <remarks>
/// <see cref="Parent"/> is the bone on the owning clip's skeleton that the participant's rig hangs
/// from, and its clip is expressed in that bone's frame - which is how a weapon gets into a
/// character's hand.
/// </remarks>
public sealed class MabParticipant
{
    public required byte Kind { get; init; }

    public required string Name { get; init; }

    public required string Parent { get; init; }

    public required string Reference { get; init; }

    public required int ClipOffset { get; init; }

    /// <summary>
    /// Whether this is the prop itself rather than a second track on it. A reload names its rifle
    /// once with no reference and again per magazine with one, so instantiating every record would
    /// fill the scene with duplicate rifles.
    /// </summary>
    public bool IsPrimary => Reference.Length == 0;
}

/// <summary>
/// One skeleton's animation within a bank.
/// </summary>
/// <remarks>
/// Everything outside the fields read here is preserved verbatim, so a parsed clip writes back
/// unchanged. Layout is documented in docs/docs/file-formats/mab.md.
/// </remarks>
public class MabClip
{
    public const int ClipHeader = 0xA0;
    public const int MaskWords = 5;
    public const int MaskCount = 4;
    public const int SectionCount = 9;

    /// <summary>Where the body tag sits in a clip.</summary>
    public const int OffsetTag = 0x70;

    public const int MaskConstantRotation = 0;
    public const int MaskKeyframeRotation = 1;
    public const int MaskConstantTranslation = 2;
    public const int MaskAnimatedTranslation = 3;

    public const int SectionRootTranslation = 0;
    public const int SectionRootRotation = 1;
    public const int SectionConstantRotation = 2;
    public const int SectionKeyframeRotation = 3;
    public const int SectionConstantTranslation = 4;
    public const int SectionAnimatedTranslation = 5;
    public const int SectionTags = 6;
    public const int SectionEvents = 7;
    public const int SectionNextClip = 8;

    /// <summary>
    /// Smallest-three quaternion codec: three components in 16 bits each over the range
    /// +/- 1/sqrt(2), with the omitted one recovered from the norm.
    /// </summary>
    /// <remarks>
    /// The scale and bias are doubles and the recovery runs in double, narrowing only once at the
    /// end. Doing it in single instead rounds at every step and drifts a few thousand ULPs off - far
    /// below anything visible, but enough that two implementations would stop agreeing.
    /// </remarks>
    public const double QuatScale = 4.315969e-05;
    public const double QuatBias = 0.70710677;
    public const int QuatBytes = 6;

    public const int Vec3Bytes = 12;

    /// <summary>Every array section opens with the same eight bytes.</summary>
    public const int TrackHeader = 8;

    /// <summary>Sparse rotation keyframes are grouped in eights, one presence byte per track.</summary>
    public const int GroupShift = 3;
    public const int GroupFrames = 1 << GroupShift;

    public const int TagCountBytes = 4;
    public const int TagStride = 0xAC;
    public const int TagKind = 0x00;
    public const int TagClip = 0x0C;
    public const int TagNameBytes = 32;

    /// <summary>
    /// Four name slots per record, each a CRC32 then 32 NUL-padded bytes: what is animated, the bone
    /// it hangs from, an always-empty slot, and a reference.
    /// </summary>
    public static ReadOnlySpan<int> TagNames => [0x18, 0x3C, 0x60, 0x84];

    /// <summary>
    /// Where the three stored components and the recovered one land in xyzw, keyed by the sign bits
    /// of the first two words.
    /// </summary>
    /// <remarks>
    /// Unit norm cannot discriminate a permutation, so this was scored against the skeleton rest
    /// pose rather than inferred: a bone a clip holds constant should sit at or near its
    /// <c>m_ChildToParent</c>, and this layout scores mean |dot| 0.977 against 0.858 for the next
    /// candidate and 0.04 for the wrong ones.
    /// </remarks>
    public static readonly int[][] EngineLayout =
    [
        [3, 0, 1, 2], [0, 1, 3, 2], [0, 3, 1, 2], [0, 1, 2, 3],
    ];

    protected static ReadOnlySpan<byte> BodyTag => "AnD\x1a"u8;

    /// <summary>The trajectory sections carry one unnamed track rather than a masked set.</summary>
    private const int RootBone = 0;

    public uint[][] Masks { get; private set; } =
        [.. Enumerable.Range(0, MaskCount).Select(_ => new uint[MaskWords])];

    public float[] ReferenceRotation { get; private set; } = [0.0f, 0.0f, 0.0f, 1.0f];

    public float[] LoopRotation { get; private set; } = [0.0f, 0.0f, 0.0f, 1.0f];

    public float Duration { get; set; }

    public int[] Sections { get; private set; } = new int[SectionCount];

    public byte[] Data { get; set; } = [];

    public static MabClip ParseClip(byte[] body)
    {
        var self = new MabClip();
        self.ReadClip(body);
        return self;
    }

    public virtual byte[] Write()
    {
        var w = new ByteWriter();
        foreach (uint[] mask in Masks)
        {
            w.WriteU32Array(mask);
        }
        w.WriteF32Array(ReferenceRotation);
        w.WriteF32Array(LoopRotation);
        w.WriteRaw(BodyTag);
        w.WriteF32(Duration);
        foreach (int offset in Sections)
        {
            w.WriteI32(offset);
        }
        // The slot the engine parks its own pointer in is zero on disk.
        w.WriteI32(0);
        w.WriteRaw(Data);
        return w.ToArray();
    }

    /// <summary>
    /// Bytes of one section, or null when the slot is unused.
    /// </summary>
    /// <remarks>
    /// A slot whose offset lands exactly at the end of the clip's data describes an empty section,
    /// and every caller has to treat that as absent rather than as something to parse - the last
    /// clip in a chain ends precisely that way, with its next-clip slot pointing at its own end.
    /// </remarks>
    public byte[]? Section(int index)
    {
        int offset = Sections[index];
        if (offset <= 0)
        {
            return null;
        }

        int end = ClipHeader + Data.Length;
        foreach (int candidate in Sections)
        {
            if (candidate > offset && candidate < end)
            {
                end = candidate;
            }
        }
        return end > offset ? Data[(offset - ClipHeader)..(end - ClipHeader)] : null;
    }

    /// <summary>(track count, last frame, frames per second) for an array section.</summary>
    public (int Tracks, int LastFrame, int Rate)? TrackHeaderOf(int index)
    {
        byte[]? block = Section(index);
        if (block is null || block.Length < TrackHeader)
        {
            return null;
        }
        return (U16(block, 0), U16(block, 2), U16(block, 4));
    }

    /// <summary>Every skeleton bone id this clip addresses, in ascending order.</summary>
    public List<int> BoneIds()
        => [.. Masks.SelectMany(MaskBones).Distinct().Order()];

    public List<int> ConstantBones() => MaskBones(Masks[MaskConstantRotation]);

    public List<int> KeyframedBones() => MaskBones(Masks[MaskKeyframeRotation]);

    /// <summary>Bone id to the single rotation held for the whole clip.</summary>
    public Dictionary<int, float[]> ConstantRotations()
        => Constant(SectionConstantRotation, MaskConstantRotation, QuatBytes, ReadQuaternion);

    /// <summary>Bone id to the single offset held for the whole clip.</summary>
    public Dictionary<int, float[]> ConstantTranslations()
        => Constant(SectionConstantTranslation, MaskConstantTranslation, Vec3Bytes, ReadVec3);

    /// <summary>
    /// Bone id to its keyed rotations, read out of the sparse groups.
    /// </summary>
    /// <remarks>
    /// Frames are grouped in eights. Each group stores, per track in bone-id order, the rotation at
    /// its first frame; then a presence byte per track, padded to an even count; then the rotations
    /// for the subframes those bytes name, again in track order. Bit i of a presence byte means a
    /// key at subframe i + 1, and the group's own first frame is always present and stored up front.
    /// </remarks>
    public Dictionary<int, List<(int Frame, float[]? Rotation)>> KeyframeTracks()
    {
        byte[]? block = Section(SectionKeyframeRotation);
        List<int> bones = KeyframedBones();
        if (block is null || bones.Count == 0 || TrackHeaderOf(SectionKeyframeRotation) is not { } header)
        {
            return [];
        }
        if (header.Tracks != bones.Count)
        {
            throw new InvalidDataException(
                $"{header.Tracks} tracks for {bones.Count} bones in the keyframe mask.");
        }

        int groups = (header.LastFrame >> GroupShift) + 1;
        Dictionary<int, List<(int, float[]?)>> out_ = bones.ToDictionary(bone => bone, _ => new List<(int, float[]?)>());
        for (int group = 0; group < groups; group++)
        {
            int start = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(TrackHeader + (group * 4)));
            int presence = start + (header.Tracks * QuatBytes);
            int cursor = presence + ((header.Tracks + 1) & ~1);
            int first = group << GroupShift;
            for (int slot = 0; slot < bones.Count; slot++)
            {
                List<(int, float[]?)> track = out_[bones[slot]];
                track.Add((first, ReadQuaternion(block, start + (slot * QuatBytes))));
                byte present = block[presence + slot];
                for (int bit = 0; bit < GroupFrames - 1; bit++)
                {
                    if (((present >> bit) & 1) != 0)
                    {
                        track.Add((first + bit + 1, ReadQuaternion(block, cursor)));
                        cursor += QuatBytes;
                    }
                }
            }
        }
        return out_;
    }

    /// <summary>Bone id to its offsets, one entry per frame with no gaps.</summary>
    public Dictionary<int, List<(int Frame, float[]? Value)>> TranslationTracks()
        => Dense(SectionAnimatedTranslation, MaskBones(Masks[MaskAnimatedTranslation]), Vec3Bytes, ReadVec3);

    /// <summary>The trajectory the clip drives the actor along.</summary>
    public List<(int Frame, float[]? Value)> RootTranslation()
        => Dense(SectionRootTranslation, [RootBone], Vec3Bytes, ReadVec3).GetValueOrDefault(RootBone, []);

    /// <summary>The heading that trajectory is turned to.</summary>
    public List<(int Frame, float[]? Value)> RootRotation()
        => Dense(SectionRootRotation, [RootBone], QuatBytes, ReadQuaternion).GetValueOrDefault(RootBone, []);

    /// <summary>What this clip animates besides its own skeleton, in chain order.</summary>
    public List<MabParticipant> Participants()
    {
        byte[]? block = Section(SectionTags);
        if (block is null)
        {
            return [];
        }

        List<MabParticipant> out_ = [];
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(block);
        for (int index = 0; index < count; index++)
        {
            int base_ = TagCountBytes + (index * TagStride);
            if (base_ + TagStride > block.Length)
            {
                break;
            }

            // The record stores its clip relative to itself; carry it relative to this
            // clip instead, which is the frame Section and Data are in.
            int offset = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(base_ + TagClip));
            out_.Add(new MabParticipant
            {
                Kind = block[base_ + TagKind],
                Name = TagName(block, base_ + TagNames[0]),
                Parent = TagName(block, base_ + TagNames[1]),
                Reference = TagName(block, base_ + TagNames[3]),
                ClipOffset = Sections[SectionTags] + base_ + offset,
            });
        }
        return out_;
    }

    /// <summary>Each participant and the clip it names.</summary>
    public List<(MabParticipant Participant, MabClip Clip)> ParticipantClips()
        => [.. Participants().Select(p => (p, ParseClip(Data[(p.ClipOffset - ClipHeader)..])))];

    /// <summary>The next skeleton's clip in the bank, or null at the end.</summary>
    public MabClip? NextClip()
        => Section(SectionNextClip) is { } block ? ParseClip(block) : null;

    /// <summary>Every clip in the bank, this one first.</summary>
    public List<MabClip> Clips()
    {
        List<MabClip> out_ = [];
        for (MabClip? clip = this; clip is not null; clip = clip.NextClip())
        {
            out_.Add(clip);
        }
        return out_;
    }

    public byte[]? Tags() => Section(SectionTags);

    public byte[]? Events() => Section(SectionEvents);

    /// <summary>Skeleton bone ids named by a five-word bitmask, in ascending order.</summary>
    public static List<int> MaskBones(uint[] mask)
    {
        List<int> bones = [];
        for (int word = 0; word < mask.Length; word++)
        {
            for (int bit = 0; bit < 32; bit++)
            {
                if (((mask[word] >> bit) & 1) != 0)
                {
                    bones.Add((word * 32) + bit);
                }
            }
        }
        return bones;
    }

    /// <summary>A bone's index within its section: the popcount of the mask below it.</summary>
    public static int? MaskSlot(uint[] mask, int boneId)
    {
        (int word, int bit) = Math.DivRem(boneId, 32);
        if (((mask[word] >> bit) & 1) == 0)
        {
            return null;
        }

        int below = 0;
        for (int i = 0; i < word; i++)
        {
            below += System.Numerics.BitOperations.PopCount(mask[i]);
        }
        return below + System.Numerics.BitOperations.PopCount(mask[word] & ((1u << bit) - 1));
    }

    /// <summary>Three packed words to xyzw, or null when they do not form a rotation.</summary>
    public static float[]? UnpackQuaternion(ushort first, ushort second, short third)
    {
        double a = ((first & 0x7FFF) * QuatScale) - QuatBias;
        double b = ((second & 0x7FFF) * QuatScale) - QuatBias;
        double c = (third * QuatScale) - QuatBias;
        double remainder = 1.0 - (a * a) - (b * b) - (c * c);
        if (remainder < 0.0)
        {
            return null;
        }

        double[] values = [a, b, c, Math.Sqrt(remainder)];
        int[] layout = EngineLayout[((first >> 14) & 2) | ((second >> 15) & 1)];
        return
        [
            (float)values[layout[0]], (float)values[layout[1]],
            (float)values[layout[2]], (float)values[layout[3]],
        ];
    }

    public static float[]? ReadQuaternion(byte[] data, int offset)
    {
        if (offset + QuatBytes > data.Length)
        {
            return null;
        }
        return UnpackQuaternion(
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 2)),
            BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + 4)));
    }

    protected void ReadClip(byte[] body)
    {
        if (body.Length < ClipHeader || !ByteCursor.Matches(body, OffsetTag, BodyTag))
        {
            throw new InvalidDataException("Missing AnD clip tag.");
        }

        var r = new ByteCursor(body);
        var masks = new uint[MaskCount][];
        for (int i = 0; i < MaskCount; i++)
        {
            masks[i] = r.ReadU32Array(MaskWords);
        }
        Masks = masks;
        ReferenceRotation = r.ReadF32Array(4);
        LoopRotation = r.ReadF32Array(4);
        // The tag was checked above; step over it to reach the duration.
        r.Position += 4;
        Duration = r.ReadF32();
        var sections = new int[SectionCount];
        for (int i = 0; i < SectionCount; i++)
        {
            sections[i] = r.ReadI32();
        }
        Sections = sections;
        if (r.ReadI32() != 0)
        {
            throw new InvalidDataException("The slot the engine writes its own pointer to is set.");
        }
        Data = body[ClipHeader..];
    }

    private static float[]? ReadVec3(byte[] data, int offset)
    {
        if (offset + Vec3Bytes > data.Length)
        {
            return null;
        }
        return
        [
            BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset)),
            BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4)),
            BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8)),
        ];
    }

    private static string TagName(byte[] block, int at)
    {
        ReadOnlySpan<byte> text = block.AsSpan(at + 4, TagNameBytes);
        int end = text.IndexOf((byte)0);
        return System.Text.Encoding.ASCII.GetString(end < 0 ? text : text[..end]);
    }

    private static int U16(byte[] data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));

    private Dictionary<int, float[]> Constant(
        int section, int mask, int stride, Func<byte[], int, float[]?> read)
    {
        byte[]? block = Section(section);
        if (block is null)
        {
            return [];
        }

        int count = U16(block, 0);
        Dictionary<int, float[]> out_ = [];
        // MaskBones yields ids ascending, so a bone's slot is its ordinal.
        List<int> bones = MaskBones(Masks[mask]);
        for (int slot = 0; slot < bones.Count && slot < count; slot++)
        {
            if (read(block, TrackHeader + (slot * stride)) is { } value)
            {
                out_[bones[slot]] = value;
            }
        }
        return out_;
    }

    /// <summary>A frame-major section: every track's value at frame 0, then at 1, and so on.</summary>
    private Dictionary<int, List<(int Frame, float[]? Value)>> Dense(
        int section, List<int> bones, int stride, Func<byte[], int, float[]?> read)
    {
        byte[]? block = Section(section);
        if (block is null || bones.Count == 0 || TrackHeaderOf(section) is not { } header)
        {
            return [];
        }
        if (header.Tracks != bones.Count)
        {
            throw new InvalidDataException(
                $"Section {section} holds {header.Tracks} tracks for {bones.Count} bones.");
        }

        Dictionary<int, List<(int, float[]?)>> out_ =
            bones.ToDictionary(bone => bone, _ => new List<(int, float[]?)>());
        for (int frame = 0; frame <= header.LastFrame; frame++)
        {
            int base_ = TrackHeader + (frame * header.Tracks * stride);
            for (int slot = 0; slot < bones.Count; slot++)
            {
                out_[bones[slot]].Add((frame, read(block, base_ + (slot * stride))));
            }
        }
        return out_;
    }
}

/// <summary>
/// A bank on disk: a small file header, then the first clip.
/// </summary>
/// <remarks>
/// A `.mab` is a bank, not a clip - it holds one clip per skeleton taking part, chained through the
/// last section slot, so a weapon's animation rides in the clip behind the character's. 4,436
/// shipped files hold 11,261 clips and the longest chain is 35.
/// </remarks>
public sealed class MabFile : MabClip
{
    public const int HeaderSize = 16;
    public const ushort VersionFc2 = 0x4C;

    public byte[] Header { get; private set; } = [];

    public ushort Version => BinaryPrimitives.ReadUInt16LittleEndian(Header);

    public static MabFile Parse(byte[] data)
    {
        if (data.Length < HeaderSize + ClipHeader)
        {
            throw new InvalidDataException("File too small to be a .mab.");
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (version != VersionFc2)
        {
            throw new InvalidDataException($"Unsupported .mab version 0x{version:X}.");
        }

        var self = new MabFile();
        self.ReadClip(data[HeaderSize..]);
        self.Header = data[..HeaderSize];
        return self;
    }

    public override byte[] Write() => [.. Header, .. base.Write()];
}
