using System.Numerics;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Mab;
using JackAll.Tools.Skeleton;

namespace JackAll.Tests;

public class MabPoseTests
{
    /// <summary>
    /// The AK-47 reload: the character's left hand reaches out, and the rifle's magazine bone
    /// travels about a metre from the rifle's root as it is dropped - the measurement the format
    /// doc records.
    /// </summary>
    [Fact]
    public void Reload_moves_the_hand_and_drops_the_magazine()
    {
        if (Fixture.Read(MabFixtures.Reload) is not { } bankBytes
            || Fixture.Read(MabFixtures.CharacterRig) is not { } pelvisBytes
            || Fixture.Read(MabFixtures.RifleRig) is not { } rifleBytes)
        {
            return;
        }

        MabFile bank = MabFile.Parse(bankBytes);
        SkeletonFile pelvis = SkeletonFile.Parse(pelvisBytes);
        SkeletonFile rifle = SkeletonFile.Parse(rifleBytes);

        var character = new MabPose(pelvis, bank);
        int hand = pelvis.BoneByName("L Hand")!.Id;
        Vector3 start = character.WorldAt(0)[hand].Translation;
        float reach = 0;
        for (int frame = 0; frame <= character.LastFrame; frame++)
        {
            reach = Math.Max(reach, Vector3.Distance(start, character.WorldAt(frame)[hand].Translation));
        }
        Assert.InRange(reach, 0.1f, 1.0f);

        (MabParticipant participant, MabClip clip) = bank.ParticipantClips().First(p => p.Participant.IsPrimary);
        Assert.Equal("ak47", participant.Name);
        Assert.True(MabPose.Fits(rifle, clip));

        var weapon = new MabPose(rifle, clip);
        int magazine = rifle.BoneByName("CLIP")!.Id;
        float travel = 0;
        for (int frame = 0; frame <= weapon.LastFrame; frame++)
        {
            Matrix4x4[] world = weapon.WorldAt(frame);
            Assert.All(world, m => Assert.True(float.IsFinite(m.Translation.Length())));
            travel = Math.Max(travel, Vector3.Distance(world[magazine].Translation, world[0].Translation));
        }
        Assert.InRange(travel, 0.5f, 2.0f);
    }

    /// <summary>A bank's rigs are found by game path: the shared pelvis for the bank, the rifle for its prop.</summary>
    [Fact]
    public void Reload_resolves_the_pelvis_and_the_rifle_rig()
    {
        if (Fixture.Read(MabFixtures.Reload) is not { } bankBytes)
        {
            return;
        }

        // Each rig's game path, and the fixture holding it.
        Dictionary<string, string> rigs = new(StringComparer.OrdinalIgnoreCase)
        {
            // A second rig beside the pelvis, listed first so the search has to prefer the pelvis.
            [@"graphics\characters\_common\singlebone_ref.skeleton"] = MabFixtures.SingleBoneRig,
            [@"graphics\characters\_common\pelvis_ref.skeleton"] = MabFixtures.CharacterRig,
            [@"graphics\weapons\primary\ak47\ak47_ref.skeleton"] = MabFixtures.RifleRig,
        };

        MabFile bank = MabFile.Parse(bankBytes);
        BankRigs found = ClipSearch.RigsFor(
            @"graphics\characters\_common\animations\weapons\primary\ak47\3rdge_uppb_reload_nodir_prak4_i1.mab",
            bank, rigs.Keys, path => rigs.TryGetValue(path, out string? fixture) ? Fixture.Read(fixture) : null);

        Assert.NotNull(found.Owner);
        Assert.NotNull(found.Owner.BoneByName("Pelvis"));
        Assert.True(MabPose.Fits(found.Owner, bank));
        Assert.NotNull(found.Participants["ak47"].BoneByName("CLIP"));
    }
}
