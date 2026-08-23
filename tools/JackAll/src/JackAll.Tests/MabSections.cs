using System.Buffers.Binary;
using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// Rebuilds each of a clip's sections from what was decoded out of it, at its own length.
/// </summary>
/// <remarks>
/// Test scaffolding: a pack carries clips as decoded documents and builds them from those, so
/// nothing shipping needs to go from a parsed clip back to sections. What this is for is holding
/// the encoders to the bytes they came from.
/// </remarks>
internal static class MabSections
{
    public static Dictionary<int, byte[]> Intrinsic(MabClip clip)
    {
        Dictionary<int, byte[]> sections = [];

        if (clip.Section(MabClip.SectionConstantRotation) is not null)
        {
            sections[MabClip.SectionConstantRotation] =
                MabEncoder.ConstantRotations(clip.ConstantBones(), clip.ConstantRotations());
        }
        if (clip.Section(MabClip.SectionConstantTranslation) is not null)
        {
            sections[MabClip.SectionConstantTranslation] = MabEncoder.ConstantTranslations(
                MabClip.MaskBones(clip.Masks[MabClip.MaskConstantTranslation]), clip.ConstantTranslations());
        }
        if (clip.TrackHeaderOf(MabClip.SectionAnimatedTranslation) is { } dense)
        {
            sections[MabClip.SectionAnimatedTranslation] = MabEncoder.DenseTranslations(
                MabClip.MaskBones(clip.Masks[MabClip.MaskAnimatedTranslation]),
                clip.TranslationTracks(), dense.LastFrame, dense.Rate);
        }
        if (clip.TrackHeaderOf(MabClip.SectionRootRotation) is { } rotation)
        {
            sections[MabClip.SectionRootRotation] =
                MabEncoder.DenseRotations(clip.RootRotation(), rotation.LastFrame, rotation.Rate);
        }
        if (clip.TrackHeaderOf(MabClip.SectionRootTranslation) is { } translation)
        {
            sections[MabClip.SectionRootTranslation] = MabEncoder.DenseTranslations(
                [0], new Dictionary<int, List<(int, float[]?)>> { [0] = clip.RootTranslation() },
                translation.LastFrame, translation.Rate);
        }
        if (clip.TrackHeaderOf(MabClip.SectionKeyframeRotation) is { } keyed)
        {
            sections[MabClip.SectionKeyframeRotation] = clip.KeyframedBones().Count > 0
                ? MabEncoder.KeyframeRotations(
                    clip.KeyframedBones(), clip.KeyframeTracks(), keyed.LastFrame, keyed.Rate)
                : throw new InvalidDataException("An empty keyframe mask cannot be rebuilt.");
        }

        // The tag table is a count and fixed-size records, so its own length is known even though
        // most of each record is not understood.
        if (clip.Section(MabClip.SectionTags) is { } tags)
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(tags);
            sections[MabClip.SectionTags] = tags[..(MabClip.TagCountBytes + (count * MabClip.TagStride))];
        }

        // The chained clip runs to the end, so what was read is what it is. The last clip in a
        // chain still names the slot - pointing at its own end - so an empty body stands for that.
        if (clip.Section(MabClip.SectionNextClip) is { } next)
        {
            sections[MabClip.SectionNextClip] = next;
        }
        else if (clip.Sections[MabClip.SectionNextClip] != 0)
        {
            sections[MabClip.SectionNextClip] = [];
        }
        return sections;
    }
}
