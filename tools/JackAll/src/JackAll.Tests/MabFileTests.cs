using JackAll.Tools.Mab;
using JackAll.Tools.Skeleton;

namespace JackAll.Tests;

/// <summary>
/// A bank re-serialises to its own bytes, and what its clips decode to has to agree with the rig
/// they were authored for.
/// </summary>
/// <remarks>
/// A bank is a chain of clips, one per participating skeleton, and a clip addresses bones by their
/// id in that skeleton rather than by name. So the round trip alone proves little here - the checks
/// that matter resolve the masks against a real rig and require the quaternions to be unit length.
/// </remarks>
public sealed class MabFileTests
{
    // A chain that stops early still round-trips, because the clips are carried as bytes - so the
    // clip count is what proves the walk reaches the end of the bank.
    [Theory]
    [InlineData(MabFixtures.Reload, 4)]
    [InlineData(MabFixtures.Pistol, 2)]
    public void Reserialises_byte_for_byte(string fixture, int clips)
    {
        if (Fixture.Read(fixture) is not { } original)
        {
            return;
        }

        MabFile bank = MabFile.Parse(original);
        Fixture.AssertSameBytes(fixture, original, bank.Write());
        Assert.Equal(clips, bank.Clips().Count);
    }

    /// <summary>
    /// Every mask bit has to name a bone the character rig actually has, and every rotation has to
    /// decode to a unit quaternion - the two checks that catch a misread mask or a wrong component
    /// layout, neither of which a round trip can see.
    /// </summary>
    [Theory]
    [InlineData(MabFixtures.Reload)]
    [InlineData(MabFixtures.Pistol)]
    public void Every_mask_bit_and_rotation_resolves_against_the_character_rig(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes || Fixture.Read(MabFixtures.CharacterRig) is not { } rigBytes)
        {
            return;
        }

        SkeletonFile rig = SkeletonFile.Parse(rigBytes);
        string[] translating = [.. rig.TranslationBoneIds
            .Where(id => id != SkeletonFile.NoBone)
            .Select(id => rig.Bones[id].Name)];

        // The first clip in a bank targets the character rig; the rest belong to whatever else
        // takes part and are addressed by their own skeletons.
        MabClip clip = MabFile.Parse(bytes);
        Assert.All(clip.BoneIds(), bone => Assert.InRange(bone, 0, rig.Bones.Count - 1));
        Assert.All(
            clip.ConstantRotations().Values,
            rotation => Assert.InRange(Math.Sqrt(rotation.Sum(v => (double)v * v)), 1.0 - 1e-6, 1.0 + 1e-6));

        // A translation may only land on a bone the rig marks as animating one.
        Assert.All(
            MabClip.MaskBones(clip.Masks[MabClip.MaskAnimatedTranslation])
                .Concat(MabClip.MaskBones(clip.Masks[MabClip.MaskConstantTranslation])),
            bone => Assert.Contains(rig.Bones[bone].Name, translating));
    }

    /// <summary>
    /// The tag table is the participant index: record i points at chained clip i, which is how a
    /// weapon gets into a character's hand.
    /// </summary>
    [Fact]
    public void Every_tag_record_reaches_the_clip_it_names()
    {
        if (Fixture.Read(MabFixtures.Reload) is not { } bytes)
        {
            return;
        }

        List<(MabParticipant Participant, MabClip Clip)> resolved = MabFile.Parse(bytes).ParticipantClips();
        Assert.Equal(3, resolved.Count);
        Assert.All(resolved, pair => Assert.NotEmpty(pair.Participant.Name));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found()
        => Fixture.AssertPresent(
            MabFixtures.Reload, MabFixtures.Pistol, MabFixtures.Tie, MabFixtures.Cutscene, MabFixtures.RifleRun,
            MabFixtures.SingleBoneRig);
}
