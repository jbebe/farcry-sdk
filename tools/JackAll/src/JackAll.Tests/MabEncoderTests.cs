using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// Each of a clip's sections, regenerated from what was decoded out of it and required back.
/// </summary>
/// <remarks>
/// Per section rather than per file, so a failure names which layout is wrong instead of a byte
/// offset. A section is compared against its own intrinsic length rather than the span to the next
/// one, because that span includes the alignment padding, which is the writer's business.
/// <para>
/// The sections carrying no rotations are held to rebuilding exactly. The two that do carry them
/// cannot always be, and the reason is in the data rather than the encoder: a rotation authored on
/// an exact tie re-encodes to a different, equally valid triple.
/// </para>
/// </remarks>
public sealed class MabEncoderTests
{
    [Theory]
    [InlineData(MabFixtures.Reload)]
    [InlineData(MabFixtures.Pistol)]
    public void Every_section_without_rotations_rebuilds_exactly(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        foreach (MabClip clip in MabFile.Parse(bytes).Clips())
        {
            Assert.True(
                Rebuilds(clip, MabClip.SectionConstantTranslation, () => MabEncoder.ConstantTranslations(
                    MabClip.MaskBones(clip.Masks[MabClip.MaskConstantTranslation]), clip.ConstantTranslations())),
                "constant translations");

            if (clip.TrackHeaderOf(MabClip.SectionAnimatedTranslation) is { } dense)
            {
                Assert.True(
                    Rebuilds(clip, MabClip.SectionAnimatedTranslation, () => MabEncoder.DenseTranslations(
                        MabClip.MaskBones(clip.Masks[MabClip.MaskAnimatedTranslation]),
                        clip.TranslationTracks(), dense.LastFrame, dense.Rate)),
                    "dense translations");
            }

            if (clip.TrackHeaderOf(MabClip.SectionRootRotation) is { } trajectory)
            {
                Assert.True(
                    Rebuilds(clip, MabClip.SectionRootRotation,
                        () => MabEncoder.DenseRotations(clip.RootRotation(), trajectory.LastFrame, trajectory.Rate)),
                    "trajectory rotation");
            }
        }
    }

    [Theory]
    [InlineData(MabFixtures.Pistol, 0)]
    [InlineData(MabFixtures.Tie, 1)]
    public void The_rotation_sections_rebuild_except_where_a_rotation_ties(string fixture, int ties)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        int missed = 0;
        foreach (MabClip clip in MabFile.Parse(bytes).Clips())
        {
            missed += Rebuilds(clip, MabClip.SectionConstantRotation,
                () => MabEncoder.ConstantRotations(clip.ConstantBones(), clip.ConstantRotations())) ? 0 : 1;

            // A clip can carry the section with an empty mask; there is nothing to rebuild.
            if (clip.TrackHeaderOf(MabClip.SectionKeyframeRotation) is { } keyed && clip.KeyframedBones().Count > 0)
            {
                missed += Rebuilds(clip, MabClip.SectionKeyframeRotation, () => MabEncoder.KeyframeRotations(
                    clip.KeyframedBones(), clip.KeyframeTracks(), keyed.LastFrame, keyed.Rate)) ? 0 : 1;
            }
        }
        Assert.Equal(ties, missed);
    }

    /// <summary>Whether the section rebuilds to the start of its slot, or the clip carries none.</summary>
    private static bool Rebuilds(MabClip clip, int slot, Func<byte[]> build)
    {
        if (clip.Section(slot) is not { } original)
        {
            return true;
        }

        byte[] produced = build();
        return produced.Length <= original.Length
            && produced.AsSpan().SequenceEqual(original.AsSpan(0, produced.Length));
    }
}
