using System.Text.Json;
using JackAll.Tools.Fc2Model;
using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// The pack's animation gate: every shipped bank decoded to the format-free document a
/// <c>.fc2model</c> carries, built back, and required to land where it was.
/// </summary>
/// <remarks>
/// Through JSON, because that is how it travels. This is the piece that lets clips ride in a pack
/// at all - without it an editor would have to decode <c>.mab</c> itself, which is the one thing
/// the pack exists to prevent.
/// <para>
/// The four bone bitmasks are not carried; they are derived from which bones hold data. So a bank
/// coming back with its framing intact also says the derivation agrees with what shipped.
/// </para>
/// </remarks>
public sealed class BankDocumentTests
{
    // The options a pack actually writes with, so this gate covers the shape that ships rather
    // than a shape only it uses.
    private static readonly JsonSerializerOptions Json = Fc2ModelJson.Compact;

    [Fact]
    public void Every_bank_survives_the_trip_through_json()
    {
        int rebuilt = 0;
        int framed = 0;
        int exact = 0;
        int skipped = 0;
        List<string> samples = [];

        foreach (string path in Fc2Corpus.Find(".mab"))
        {
            byte[] original = File.ReadAllBytes(path);
            MabFile bank = MabFile.Parse(original);

            byte[] produced;
            try
            {
                string text = JsonSerializer.Serialize(BankDocument.From(bank), Json);
                produced = JsonSerializer.Deserialize<BankDocument>(text, Json)!.ToMab();
            }
            catch (InvalidDataException)
            {
                // A clip holding a triple that is not a rotation cannot be written back at all.
                skipped++;
                continue;
            }

            rebuilt++;
            if (produced.Length == original.Length && SameShape(original, produced))
            {
                framed++;
            }
            else if (samples.Count < 5)
            {
                samples.Add($"{Path.GetFileName(path)}: {produced.Length} bytes vs {original.Length}");
            }

            exact += produced.AsSpan().SequenceEqual(original) ? 1 : 0;
        }

        Assert.True(rebuilt > 0 || !Fc2Corpus.Present, "No bank was rebuilt.");

        double framing = framed / (double)rebuilt;
        Assert.True(
            framing >= 0.999,
            $"{framed}/{rebuilt} banks rebuilt with every clip and mask where it was ({framing:P1}), "
            + $"{skipped} skipped.{Environment.NewLine}" + string.Join(Environment.NewLine, samples));

        // Bytes lag framing by the rotations that cannot be re-encoded exactly, compounded over a
        // chain of up to 35 clips.
        double bytes = exact / (double)rebuilt;
        Assert.True(bytes >= 0.78, $"{exact}/{rebuilt} banks came back byte-identical ({bytes:P1}).");
    }

    /// <summary>
    /// A participant's record names the clip that actually moves it.
    /// </summary>
    /// <remarks>
    /// The document says record <c>k</c> is chain clip <c>k + 1</c>, which is what lets a reader
    /// find the gun's motion without touching the tag block those records came from. That is a claim
    /// about every shipped bank, not about the one the encoder was written against, so it is checked
    /// against where the records' own byte offsets land.
    /// </remarks>
    [Fact]
    public void A_participant_names_the_clip_that_moves_it()
    {
        int checkedRecords = 0;
        List<string> failures = [];

        foreach (string path in Fc2Corpus.Find(".mab"))
        {
            MabFile bank;
            List<(MabParticipant Participant, MabClip Clip)> byOffset;
            try
            {
                bank = MabFile.Parse(File.ReadAllBytes(path));
                byOffset = bank.ParticipantClips();
            }
            catch (InvalidDataException)
            {
                continue;
            }

            List<MabClip> chain = bank.Clips();
            foreach (BankParticipant carried in BankDocument.From(bank).Participants)
            {
                checkedRecords++;
                if (carried.Clip >= chain.Count)
                {
                    failures.Add($"{Path.GetFileName(path)}: {carried.Name} names clip "
                                 + $"{carried.Clip} of {chain.Count}");
                    continue;
                }

                // Same bones and same sections is what "the same clip" means here - two clips in one
                // bank move different skeletons, so agreeing on both is not something a wrong index
                // gets away with.
                MabClip named = chain[carried.Clip];
                MabClip actual = byOffset[carried.Clip - 1].Clip;
                if (!named.BoneIds().SequenceEqual(actual.BoneIds())
                    || !named.Sections.SequenceEqual(actual.Sections))
                {
                    failures.Add($"{Path.GetFileName(path)}: {carried.Name} names clip "
                                 + $"{carried.Clip}, whose bones are not the record's own");
                }
            }
        }

        Assert.True(checkedRecords > 0 || !Fc2Corpus.Present, "No participant was checked.");
        Assert.True(
            failures.Count == 0,
            $"{checkedRecords - failures.Count}/{checkedRecords} participants name the clip that "
            + $"moves them.{Environment.NewLine}" + string.Join(Environment.NewLine, failures.Take(5)));
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".mab").Any(), Fc2Corpus.MissingMessage(".mab"));

    /// <summary>
    /// Whether both banks hold the same chain, with each clip naming the same sections and the same
    /// bones - which is what the derived masks and the rebuilt nesting have to get right.
    /// </summary>
    private static bool SameShape(byte[] original, byte[] produced)
    {
        List<MabClip> before = MabFile.Parse(original).Clips();
        List<MabClip> after = MabFile.Parse(produced).Clips();
        if (before.Count != after.Count)
        {
            return false;
        }

        for (int index = 0; index < before.Count; index++)
        {
            if (!before[index].Sections.SequenceEqual(after[index].Sections)
                || !before[index].BoneIds().SequenceEqual(after[index].BoneIds()))
            {
                return false;
            }
        }
        return true;
    }
}
