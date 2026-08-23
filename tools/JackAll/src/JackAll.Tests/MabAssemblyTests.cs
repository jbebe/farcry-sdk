using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// A whole clip laid out from its sections, and required to match the bytes it was read from.
/// </summary>
/// <remarks>
/// <see cref="MabEncoderTests"/> proves each section's contents; this proves the framing around
/// them - the order they sit in, the 16-byte alignment, the zero separator before the event chunk or
/// the chained clip, and the offsets the table then has to carry.
/// <para>
/// Restricted to clips with no event chunk, because that one is FCB and its length cannot be
/// computed from anything decoded - carrying it verbatim would carry its padding too and prove
/// nothing about where it starts.
/// </para>
/// </remarks>
public sealed class MabAssemblyTests
{
    [Fact]
    public void A_clip_lays_out_where_it_was_read_from()
    {
        int assembled = 0;
        int matched = 0;
        int framed = 0;
        int skipped = 0;
        List<string> samples = [];

        foreach (string path in Fc2Corpus.Find(".mab"))
        {
            MabFile bank = MabFile.Parse(File.ReadAllBytes(path));
            foreach (MabClip clip in bank.Clips())
            {
                if (clip.Section(MabClip.SectionEvents) is not null)
                {
                    skipped++;
                    continue;
                }

                Dictionary<int, byte[]> sections;
                try
                {
                    sections = MabSections.Intrinsic(clip);
                }
                catch (InvalidDataException)
                {
                    skipped++;
                    continue;
                }

                assembled++;
                (byte[] data, int[] offsets) = MabEncoder.Assemble(sections);

                // Framing and content fail for different reasons, so they are counted apart: a
                // rotation that re-encodes to a different triple changes bytes without moving
                // anything, and only the framing is this test's subject.
                if (offsets.SequenceEqual(clip.Sections) && data.Length == clip.Data.Length)
                {
                    framed++;
                }
                else if (samples.Count < 5)
                {
                    samples.Add(
                        $"{Path.GetFileName(path)}: {data.Length} bytes vs {clip.Data.Length}, "
                        + $"offsets [{string.Join(",", offsets)}] vs [{string.Join(",", clip.Sections)}]");
                }

                matched += data.AsSpan().SequenceEqual(clip.Data) ? 1 : 0;
            }
        }

        Assert.True(assembled > 0 || !Fc2Corpus.Present, "No clip was assembled.");

        double framing = framed / (double)assembled;
        Assert.True(
            framing >= 0.999,
            $"{framed}/{assembled} clips landed every section where it was read from ({framing:P1}), "
            + $"{skipped} skipped. First differences:{Environment.NewLine}"
            + string.Join(Environment.NewLine, samples));

        // Bytes lag framing by the clips whose rotations cannot be re-encoded exactly, which
        // MabEncoderTests measures directly.
        double exact = matched / (double)assembled;
        Assert.True(
            exact >= 0.90,
            $"{matched}/{assembled} clips came back byte-identical ({exact:P1}).");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".mab").Any(), Fc2Corpus.MissingMessage(".mab"));

}
