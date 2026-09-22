using System.Numerics;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Mab;
using JackAll.Tools.Skeleton;

namespace JackAll.Tests;

public class MabPoseTests
{
    private const string Reload = "3rdge_uppb_reload_nodir_prak4_i1.mab";

    /// <summary>
    /// The AK-47 reload: the character's left hand reaches out, and the rifle's magazine bone
    /// travels about a metre from the rifle's root as it is dropped - the measurement the format
    /// doc records.
    /// </summary>
    [Fact]
    public void Reload_moves_the_hand_and_drops_the_magazine()
    {
        string? bankPath = Fc2Corpus.Named(".mab", Reload);
        string? pelvisPath = Fc2Corpus.Named(".skeleton", "pelvis_ref.skeleton");
        string? riflePath = Fc2Corpus.Named(".skeleton", "ak47_ref.skeleton");
        if (bankPath is null || pelvisPath is null || riflePath is null)
        {
            return;
        }

        MabFile bank = MabFile.Parse(File.ReadAllBytes(bankPath));
        SkeletonFile pelvis = SkeletonFile.Parse(File.ReadAllBytes(pelvisPath));
        SkeletonFile rifle = SkeletonFile.Parse(File.ReadAllBytes(riflePath));

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
        string? bankPath = Fc2Corpus.Named(".mab", Reload);
        if (bankPath is null)
        {
            return;
        }

        // The corpus keeps each archive in its own folder; the game path starts at graphics\.
        static string GamePath(string diskPath)
            => diskPath[diskPath.IndexOf("graphics", StringComparison.OrdinalIgnoreCase)..].Replace('/', '\\');
        Dictionary<string, string> rigs = new(StringComparer.OrdinalIgnoreCase);
        foreach (string disk in Fc2Corpus.Find(".skeleton").Where(p => p.Contains("graphics", StringComparison.OrdinalIgnoreCase)))
        {
            rigs.TryAdd(GamePath(disk), disk);
        }

        MabFile bank = MabFile.Parse(File.ReadAllBytes(bankPath));
        BankRigs found = ClipSearch.RigsFor(GamePath(bankPath), bank, rigs.Keys,
            path => rigs.TryGetValue(path, out string? disk) ? File.ReadAllBytes(disk) : null);

        Assert.NotNull(found.Owner);
        Assert.NotNull(found.Owner.BoneByName("Pelvis"));
        Assert.True(MabPose.Fits(found.Owner, bank));
        Assert.NotNull(found.Participants["ak47"].BoneByName("CLIP"));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Named(".mab", Reload) is not null, Fc2Corpus.MissingMessage(".mab"));
}
