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
                    sections = Intrinsic(clip);
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

    /// <summary>Each section at its own length, with the opaque ones cut to what they actually hold.</summary>
    private static Dictionary<int, byte[]> Intrinsic(MabClip clip)
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
            int count = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(tags);
            sections[MabClip.SectionTags] =
                tags[..(MabClip.TagCountBytes + (count * MabClip.TagStride))];
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
