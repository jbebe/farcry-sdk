using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// A whole bank rebuilt from its decoded clips: the chain nested back together and the tag table
/// repointed at where each clip landed.
/// </summary>
/// <remarks>
/// This is the piece a clip writer needs and the one that fails quietly. A chain is nested, so
/// changing any clip's size moves every clip after it, and each tag record carries its clip as a
/// delta from the record's own position - get one wrong and the animation misbehaves without
/// crashing, the same failure mode as an unsorted depload.
/// <para>
/// Banks carrying an event chunk are skipped: it is FCB, its length is not computable from anything
/// decoded, and carrying it verbatim would carry its padding too.
/// </para>
/// </remarks>
public sealed class MabBankTests
{
    // Measures a rate, so a corpus of no files divides by zero rather than no-opping.
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Every_bank_rebuilds_with_its_chain_and_tags_intact()
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
            List<MabClip> chain = bank.Clips();

            List<MabClipParts> parts = [];
            bool usable = true;
            foreach (MabClip clip in chain)
            {
                if (clip.Section(MabClip.SectionEvents) is not null)
                {
                    usable = false;
                    break;
                }
                try
                {
                    parts.Add(MabClipParts.Of(clip, MabSections.Intrinsic(clip)));
                }
                catch (InvalidDataException)
                {
                    usable = false;
                    break;
                }
            }

            if (!usable)
            {
                skipped++;
                continue;
            }

            rebuilt++;
            byte[] produced = MabEncoder.AssembleBank(bank.Header, parts);

            if (produced.Length == original.Length && SameFraming(original, produced))
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
            framing >= 0.99,
            $"{framed}/{rebuilt} banks rebuilt with every clip and tag where it was ({framing:P1}), "
            + $"{skipped} skipped.{Environment.NewLine}" + string.Join(Environment.NewLine, samples));

        // Bytes lag framing by the rotations that cannot be re-encoded exactly, and a bank compounds
        // that: one tie anywhere in a chain of up to 35 clips fails the whole file, so the per-bank
        // rate sits well under the per-clip one.
        double bytes = exact / (double)rebuilt;
        Assert.True(bytes >= 0.75, $"{exact}/{rebuilt} banks came back byte-identical ({bytes:P1}).");
    }

    /// <summary>
    /// A bank re-laid from its sections' own bytes has to come back exactly.
    /// </summary>
    /// <remarks>
    /// This is what makes rewriting one clip safe. A bank holds the character's motion as well as
    /// the weapon's, and re-encoding the lot loses bytes on a fifth of the shipped set - so a writer
    /// that rebuilt everything would perturb clips nobody touched. Carrying an untouched clip's
    /// sections verbatim instead lands them exactly where they were, and the only clip re-encoded is
    /// the one somebody edited.
    /// <para>
    /// It only works at a section's *intrinsic* length: the block a reader slices runs to wherever
    /// the next section starts, so it carries the alignment padding and the separator with it, and
    /// re-laying those adds them a second time - one separator per clip, on every shipped bank.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_bank_relaid_from_its_own_section_bytes_is_unchanged()
    {
        int rebuilt = 0;
        List<string> failures = [];

        foreach (string path in Fc2Corpus.Find(".mab"))
        {
            byte[] original = File.ReadAllBytes(path);
            MabFile bank = MabFile.Parse(original);

            List<MabClipParts> parts = [.. bank.Clips().Select(
                clip => MabClipParts.Of(clip, Verbatim(clip)))];

            rebuilt++;
            byte[] produced = MabEncoder.AssembleBank(bank.Header, parts);
            if (!produced.AsSpan().SequenceEqual(original) && failures.Count < 5)
            {
                failures.Add(Fc2Corpus.DescribeDifference(path, original, produced));
            }
        }

        Assert.True(rebuilt > 0 || !Fc2Corpus.Present, "No bank was re-laid.");
        Assert.True(
            failures.Count == 0,
            $"{rebuilt - failures.Count}/{rebuilt} banks came back byte-identical."
            + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>Every section a clip carries, at its own length.</summary>
    private static Dictionary<int, byte[]> Verbatim(MabClip clip)
    {
        Dictionary<int, byte[]> sections = [];
        for (int slot = 0; slot < MabClip.SectionCount; slot++)
        {
            if (slot == MabClip.SectionNextClip)
            {
                continue;
            }
            if (clip.IntrinsicSection(slot) is { } bytes)
            {
                sections[slot] = bytes;
            }
            else if (clip.Sections[slot] != 0)
            {
                // A slot that names an empty section still has to be named back.
                sections[slot] = [];
            }
        }
        return sections;
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".mab").Any(), Fc2Corpus.MissingMessage(".mab"));

    /// <summary>
    /// Whether every clip in both chains sits at the same offset and names the same sections - which
    /// is what the tag deltas and the nesting have to get right, independent of the bytes inside.
    /// </summary>
    private static bool SameFraming(byte[] original, byte[] produced)
    {
        List<MabClip> before = MabFile.Parse(original).Clips();
        List<MabClip> after = MabFile.Parse(produced).Clips();
        if (before.Count != after.Count)
        {
            return false;
        }

        for (int index = 0; index < before.Count; index++)
        {
            if (!before[index].Sections.SequenceEqual(after[index].Sections))
            {
                return false;
            }
        }

        // Every tag record has to reach the clip it names, which is what the deltas encode.
        List<MabParticipant> expected = MabFile.Parse(original).Participants();
        List<MabParticipant> got = MabFile.Parse(produced).Participants();
        return expected.Count == got.Count
            && expected.Zip(got).All(pair => pair.First.ClipOffset == pair.Second.ClipOffset);
    }
}
