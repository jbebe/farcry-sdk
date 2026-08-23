using JackAll.Tools.Mab;

namespace JackAll.Tools.Fc2Model;

/// <summary>One bone's value held for a whole clip.</summary>
public sealed class ClipConstant
{
    public required int Bone { get; init; }

    /// <summary>Four for a rotation, three for an offset.</summary>
    public required float[] Value { get; init; }
}

/// <summary>
/// One bone's keys, flat.
/// </summary>
/// <remarks>
/// Frames and values are parallel arrays rather than a list of pairs, for the same reason the mesh
/// buffers are flat: a tuple's members are fields, so a general serialiser writes each key as an
/// empty object and loses the lot.
/// </remarks>
public sealed class ClipTrack
{
    public required int Bone { get; init; }

    public required int[] Frames { get; init; }

    /// <summary>Four per key for a rotation, three for an offset.</summary>
    public required float[] Values { get; init; }
}

/// <summary>A keyed section's own timing, which need not match its siblings'.</summary>
public sealed class ClipTiming
{
    public required int LastFrame { get; init; }

    public required int Rate { get; init; }
}

/// <summary>
/// One clip with no Dunia bytes in it bar the two blocks nothing decodes.
/// </summary>
/// <remarks>
/// The four bone bitmasks are left out and derived on the way back from which bones actually carry
/// data, so an editor adding or dropping a bone does not have to know they exist.
/// <para>
/// The tag table and the event chain travel as bytes: 140 of the 172 bytes in a tag record are still
/// undecoded, and an event chain is FCB. They are small opaque fields rather than a container, and
/// an editor has no reason to touch either.
/// </para>
/// </remarks>
public sealed class ClipDocument
{
    public required float[] ReferenceRotation { get; init; }

    public required float[] LoopRotation { get; init; }

    public required float Duration { get; init; }

    public List<ClipConstant> ConstantRotations { get; init; } = [];

    public List<ClipConstant> ConstantTranslations { get; init; } = [];

    public List<ClipTrack> KeyframeRotations { get; init; } = [];

    public ClipTiming? KeyframeTiming { get; init; }

    public List<ClipTrack> AnimatedTranslations { get; init; } = [];

    public ClipTiming? TranslationTiming { get; init; }

    public ClipTrack? RootTranslation { get; init; }

    public ClipTiming? RootTranslationTiming { get; init; }

    public ClipTrack? RootRotation { get; init; }

    public ClipTiming? RootRotationTiming { get; init; }

    public byte[]? Tags { get; init; }

    public byte[]? Events { get; init; }

    /// <summary>Whether the clip named a next-clip slot, which the last one still does.</summary>
    public bool Chained { get; init; }

    /// <summary>
    /// Which section slots the clip carries.
    /// </summary>
    /// <remarks>
    /// Not derivable from the data, because a clip can carry a section that holds nothing - 451 of
    /// 982 sampled clips name a keyframe section whose mask is empty. Whether the slot is there is a
    /// fact about the clip; what is in it is the rest of this document.
    /// </remarks>
    public List<int> Sections { get; init; } = [];

    public static ClipDocument From(MabClip clip) => new()
    {
        ReferenceRotation = [.. clip.ReferenceRotation],
        LoopRotation = [.. clip.LoopRotation],
        Duration = clip.Duration,
        ConstantRotations = Constants(clip.ConstantRotations()),
        ConstantTranslations = Constants(clip.ConstantTranslations()),
        KeyframeRotations = Tracks(clip.KeyframeTracks(), 4),
        KeyframeTiming = Timing(clip, MabClip.SectionKeyframeRotation),
        AnimatedTranslations = Tracks(clip.TranslationTracks(), 3),
        TranslationTiming = Timing(clip, MabClip.SectionAnimatedTranslation),
        RootTranslation = Track(0, clip.RootTranslation(), 3),
        RootTranslationTiming = Timing(clip, MabClip.SectionRootTranslation),
        RootRotation = Track(0, clip.RootRotation(), 4),
        RootRotationTiming = Timing(clip, MabClip.SectionRootRotation),
        Tags = TagBytes(clip),
        // The event chain's own length is not computable, so its span travels as it stands - which
        // is already aligned, so re-emitting it lands in the same place.
        Events = clip.Section(MabClip.SectionEvents),
        Chained = clip.Sections[MabClip.SectionNextClip] != 0,
        Sections = [.. Enumerable.Range(0, MabClip.SectionCount)
            .Where(slot => slot != MabClip.SectionNextClip && clip.Sections[slot] != 0)],
    };

    /// <summary>The clip's parts, with the four bitmasks derived from what actually carries data.</summary>
    public MabClipParts ToParts()
    {
        // Driven by which slots the clip carries rather than by which hold data, so a section that
        // exists and names nothing is written back as the empty section it was.
        Dictionary<int, byte[]> sections = [];
        foreach (int slot in Sections)
        {
            sections[slot] = slot switch
            {
                MabClip.SectionConstantRotation => MabEncoder.ConstantRotations(
                    [.. ConstantRotations.Select(c => c.Bone)],
                    ConstantRotations.ToDictionary(c => c.Bone, c => c.Value)),

                MabClip.SectionConstantTranslation => MabEncoder.ConstantTranslations(
                    [.. ConstantTranslations.Select(c => c.Bone)],
                    ConstantTranslations.ToDictionary(c => c.Bone, c => c.Value)),

                MabClip.SectionKeyframeRotation => MabEncoder.KeyframeRotations(
                    [.. KeyframeRotations.Select(t => t.Bone)],
                    KeyframeRotations.ToDictionary(t => t.Bone, t => Keys(t, 4)),
                    KeyframeTiming?.LastFrame ?? 0, KeyframeTiming?.Rate ?? 0),

                MabClip.SectionAnimatedTranslation => MabEncoder.DenseTranslations(
                    [.. AnimatedTranslations.Select(t => t.Bone)],
                    AnimatedTranslations.ToDictionary(t => t.Bone, t => Keys(t, 3)),
                    TranslationTiming?.LastFrame ?? 0, TranslationTiming?.Rate ?? 0),

                MabClip.SectionRootTranslation => MabEncoder.DenseTranslations(
                    RootTranslation is null ? [] : [0],
                    RootTranslation is null
                        ? []
                        : new Dictionary<int, List<(int, float[]?)>> { [0] = Keys(RootTranslation, 3) },
                    RootTranslationTiming?.LastFrame ?? 0, RootTranslationTiming?.Rate ?? 0),

                MabClip.SectionRootRotation => MabEncoder.DenseRotations(
                    RootRotation is null ? [] : Keys(RootRotation, 4),
                    RootRotationTiming?.LastFrame ?? 0, RootRotationTiming?.Rate ?? 0),

                MabClip.SectionTags => Tags ?? [],
                MabClip.SectionEvents => Events ?? [],
                _ => throw new InvalidDataException($"A clip cannot carry section {slot}."),
            };
        }

        return new MabClipParts
        {
            Masks = Masks(),
            ReferenceRotation = [.. ReferenceRotation],
            LoopRotation = [.. LoopRotation],
            Duration = Duration,
            Sections = sections,
        };
    }

    /// <summary>
    /// The four bitmasks, rebuilt from which bones carry what.
    /// </summary>
    /// <remarks>
    /// A mask is indexed by skeleton bone id, and a bone's slot inside its section is the popcount
    /// of the mask below it - so the mask and the section's ordering are the same fact stated twice,
    /// and deriving it is what keeps them from disagreeing.
    /// </remarks>
    private uint[][] Masks()
    {
        var masks = new uint[MabClip.MaskCount][];
        for (int index = 0; index < MabClip.MaskCount; index++)
        {
            masks[index] = new uint[MabClip.MaskWords];
        }

        Set(masks[MabClip.MaskConstantRotation], ConstantRotations.Select(c => c.Bone));
        Set(masks[MabClip.MaskKeyframeRotation], KeyframeRotations.Select(t => t.Bone));
        Set(masks[MabClip.MaskConstantTranslation], ConstantTranslations.Select(c => c.Bone));
        Set(masks[MabClip.MaskAnimatedTranslation], AnimatedTranslations.Select(t => t.Bone));
        return masks;
    }

    private static void Set(uint[] mask, IEnumerable<int> bones)
    {
        foreach (int bone in bones)
        {
            mask[bone / 32] |= 1u << (bone % 32);
        }
    }

    private static byte[]? TagBytes(MabClip clip)
    {
        if (clip.Section(MabClip.SectionTags) is not { } tags)
        {
            return null;
        }
        int count = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(tags);
        return tags[..(MabClip.TagCountBytes + (count * MabClip.TagStride))];
    }

    private static ClipTiming? Timing(MabClip clip, int slot)
        => clip.TrackHeaderOf(slot) is { } header
            ? new ClipTiming { LastFrame = header.LastFrame, Rate = header.Rate }
            : null;

    private static List<ClipConstant> Constants(IReadOnlyDictionary<int, float[]> values)
        => [.. values.OrderBy(pair => pair.Key)
            .Select(pair => new ClipConstant { Bone = pair.Key, Value = pair.Value })];

    private static List<ClipTrack> Tracks(
        IReadOnlyDictionary<int, List<(int Frame, float[]? Value)>> tracks, int width)
        => [.. tracks.OrderBy(pair => pair.Key).Select(pair => Track(pair.Key, pair.Value, width)!)];

    private static ClipTrack? Track(int bone, IReadOnlyList<(int Frame, float[]? Value)> keys, int width)
    {
        if (keys.Count == 0)
        {
            return null;
        }

        var frames = new int[keys.Count];
        var values = new float[keys.Count * width];
        for (int index = 0; index < keys.Count; index++)
        {
            frames[index] = keys[index].Frame;
            // A key the decoder could not read comes back as zeros; it cannot be written either way.
            float[] value = keys[index].Value ?? new float[width];
            value.AsSpan(0, width).CopyTo(values.AsSpan(index * width));
        }
        return new ClipTrack { Bone = bone, Frames = frames, Values = values };
    }

    private static List<(int Frame, float[]? Value)> Keys(ClipTrack track, int width)
        => [.. track.Frames.Select((frame, index) =>
            (frame, (float[]?)track.Values.AsSpan(index * width, width).ToArray()))];
}
