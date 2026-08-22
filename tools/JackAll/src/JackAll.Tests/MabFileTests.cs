using JackAll.Tools.Mab;
using JackAll.Tools.Skeleton;

namespace JackAll.Tests;

/// <summary>
/// The animation gate: every shipped bank re-serialises to its own bytes, and what the clips decode
/// to has to agree with the rig they were authored for.
/// </summary>
/// <remarks>
/// A bank is a chain of clips, one per participating skeleton, and a clip addresses bones by their
/// id in that skeleton rather than by name. So the round trip alone proves little here - the checks
/// that matter resolve the masks against a real rig and require the quaternions to be unit length.
/// The Python codec reaches 4,436 of 4,436.
/// </remarks>
public sealed class MabFileTests
{
    private const string Extension = ".mab";

    [Fact]
    public void Reserialises_every_shipped_bank_byte_for_byte()
    {
        List<string> failures = [];
        int checkedFiles = 0;
        int clips = 0;

        foreach (string path in Fc2Corpus.Find(Extension))
        {
            checkedFiles++;
            byte[] original = File.ReadAllBytes(path);
            try
            {
                MabFile bank = MabFile.Parse(original);
                clips += bank.Clips().Count;
                if (!bank.Write().AsSpan().SequenceEqual(original))
                {
                    failures.Add(Fc2Corpus.DescribeDifference(path, original, bank.Write()));
                }
            }
            catch (Exception error)
            {
                failures.Add($"{Path.GetFileName(path)}: {error.Message}");
            }
        }

        Assert.True(
            checkedFiles > 0 || !Fc2Corpus.Present,
            $"{Fc2Corpus.Root} holds no *{Extension}, so this gate asserted nothing.");

        Assert.True(
            failures.Count == 0,
            $"{checkedFiles - failures.Count}/{checkedFiles} banks round-tripped, holding {clips} "
            + $"clips. First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));

        // A chain that stops early still round-trips, because the clips are carried as bytes -
        // so the count is what actually proves the walk reaches the end of every bank.
        if (checkedFiles == 4436)
        {
            Assert.Equal(11261, clips);
        }
    }

    /// <summary>
    /// Every mask bit has to name a bone the character rig actually has, and every rotation has to
    /// decode to a unit quaternion - the two checks that catch a misread mask or a wrong component
    /// layout, neither of which a round trip can see.
    /// </summary>
    [Fact]
    public void Every_mask_bit_and_rotation_resolves_against_the_character_rig()
    {
        string? rigPath = Fc2Corpus.Find(".skeleton")
            .FirstOrDefault(p => Path.GetFileName(p).Equals("pelvis_ref.skeleton", StringComparison.OrdinalIgnoreCase));
        if (rigPath is null)
        {
            return;
        }

        SkeletonFile rig = SkeletonFile.Parse(File.ReadAllBytes(rigPath));
        string[] translating = [.. rig.TranslationBoneIds
            .Where(id => id != SkeletonFile.NoBone)
            .Select(id => rig.Bones[id].Name)];

        int bits = 0;
        int rotations = 0;
        double worstNorm = 0.0;
        List<string> failures = [];

        foreach (string path in Fc2Corpus.Find(Extension))
        {
            MabFile bank = MabFile.Parse(File.ReadAllBytes(path));
            // The first clip in a bank targets the character rig; the rest belong to
            // whatever else takes part and are addressed by their own skeletons.
            MabClip clip = bank;
            foreach (int bone in clip.BoneIds())
            {
                bits++;
                if (bone >= rig.Bones.Count)
                {
                    failures.Add($"{Path.GetFileName(path)}: bone id {bone} is outside the rig");
                }
            }

            foreach (float[] rotation in clip.ConstantRotations().Values)
            {
                rotations++;
                double norm = Math.Sqrt(rotation.Sum(v => (double)v * v));
                worstNorm = Math.Max(worstNorm, Math.Abs(norm - 1.0));
            }

            // A translation may only land on a bone the rig marks as animating one.
            foreach (int bone in MabClip.MaskBones(clip.Masks[MabClip.MaskAnimatedTranslation])
                         .Concat(MabClip.MaskBones(clip.Masks[MabClip.MaskConstantTranslation])))
            {
                if (bone < rig.Bones.Count && !translating.Contains(rig.Bones[bone].Name))
                {
                    failures.Add(
                        $"{Path.GetFileName(path)}: {rig.Bones[bone].Name} translates but the rig holds it fixed");
                }
            }
        }

        Assert.True(bits > 0 || !Fc2Corpus.Present, "No mask bit was examined.");
        Assert.True(
            failures.Count == 0,
            $"{bits} mask bits and {rotations} rotations checked. First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));
        Assert.True(worstNorm < 1e-6, $"Worst |norm - 1| was {worstNorm:E2}.");
    }

    /// <summary>
    /// The tag table is the participant index: record i points at chained clip i, which is how a
    /// weapon gets into a character's hand.
    /// </summary>
    [Fact]
    public void Every_tag_record_reaches_the_clip_it_names()
    {
        int records = 0;
        List<string> failures = [];

        foreach (string path in Fc2Corpus.Find(Extension))
        {
            MabFile bank = MabFile.Parse(File.ReadAllBytes(path));
            foreach (MabClip clip in bank.Clips())
            {
                List<MabParticipant> participants = clip.Participants();
                if (participants.Count == 0)
                {
                    continue;
                }

                try
                {
                    List<(MabParticipant Participant, MabClip Clip)> resolved = clip.ParticipantClips();
                    records += resolved.Count;
                    foreach ((MabParticipant participant, MabClip _) in resolved)
                    {
                        Assert.NotEmpty(participant.Name);
                    }
                }
                catch (Exception error)
                {
                    failures.Add($"{Path.GetFileName(path)}: {error.Message}");
                }
            }
        }

        Assert.True(records > 0 || !Fc2Corpus.Present, "No tag record was examined.");
        Assert.True(
            failures.Count == 0,
            $"{records} records resolved. First failures:{Environment.NewLine}"
            + string.Join(Environment.NewLine, failures.Take(5)));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(Extension).Any(), Fc2Corpus.MissingMessage(Extension));
}
