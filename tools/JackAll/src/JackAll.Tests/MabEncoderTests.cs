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
/// cannot be, and the reason is in the data rather than the encoder: a rotation authored on an exact
/// tie re-encodes to a different, equally valid triple, and 126 stored triples across three files
/// are not unit rotations at all, so nothing can write them back. Both are measured here rather than
/// assumed, and the thresholds sit just under what the corpus actually reaches.
/// </para>
/// </remarks>
public sealed class MabEncoderTests
{
    [Fact]
    public void Every_section_without_rotations_rebuilds_exactly()
    {
        Tally tally = Rebuild();

        Assert.True(tally.Seen("dense translations") > 0 || !Fc2Corpus.Present, "No section was examined.");
        foreach (string what in (string[])["constant translations", "dense translations", "trajectory rotation"])
        {
            Assert.True(
                tally.Missed(what) == 0,
                $"{what}: {tally.Missed(what)} of {tally.Seen(what)} did not rebuild."
                + Environment.NewLine + string.Join(Environment.NewLine, tally.Samples.Take(4)));
        }
    }

    [Fact]
    public void The_rotation_sections_rebuild_wherever_the_decode_was_lossless()
    {
        Tally tally = Rebuild();
        if (tally.Seen("keyframe rotations") == 0)
        {
            return;
        }

        // A clip holding a triple that is not a rotation cannot be written back at all; there are
        // 126 such keys across three files, and they are excluded rather than counted as failures.
        // Measured at 24: seventeen clips holding a triple that is not a rotation, plus seven whose
        // section count falls short of its own mask.
        Assert.True(tally.Unencodable <= 30, $"{tally.Unencodable} clips could not be encoded at all.");

        foreach ((string what, double floor) in ((string, double)[])
                 [("constant rotations", 0.95), ("keyframe rotations", 0.90)])
        {
            int seen = tally.Seen(what);
            double exact = (seen - tally.Missed(what)) / (double)seen;
            Assert.True(
                exact >= floor,
                $"{what}: {seen - tally.Missed(what)}/{seen} ({exact:P1}) rebuilt byte-exactly; "
                + $"expected at least {floor:P0}.");
        }
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".mab").Any(), Fc2Corpus.MissingMessage(".mab"));

    private static Tally Rebuild()
    {
        var tally = new Tally();
        foreach (string path in Fc2Corpus.Find(".mab"))
        {
            MabFile bank = MabFile.Parse(File.ReadAllBytes(path));
            foreach (MabClip clip in bank.Clips())
            {
                tally.Compare("constant rotations", clip, MabClip.SectionConstantRotation, path,
                    () => MabEncoder.ConstantRotations(clip.ConstantBones(), clip.ConstantRotations()));

                tally.Compare("constant translations", clip, MabClip.SectionConstantTranslation, path,
                    () => MabEncoder.ConstantTranslations(
                        MabClip.MaskBones(clip.Masks[MabClip.MaskConstantTranslation]), clip.ConstantTranslations()));

                if (clip.TrackHeaderOf(MabClip.SectionAnimatedTranslation) is { } dense)
                {
                    tally.Compare("dense translations", clip, MabClip.SectionAnimatedTranslation, path,
                        () => MabEncoder.DenseTranslations(
                            MabClip.MaskBones(clip.Masks[MabClip.MaskAnimatedTranslation]),
                            clip.TranslationTracks(), dense.LastFrame, dense.Rate));
                }

                if (clip.TrackHeaderOf(MabClip.SectionRootRotation) is { } trajectory)
                {
                    tally.Compare("trajectory rotation", clip, MabClip.SectionRootRotation, path,
                        () => MabEncoder.DenseRotations(clip.RootRotation(), trajectory.LastFrame, trajectory.Rate));
                }

                // A clip can carry the section with an empty mask; there is nothing to rebuild.
                if (clip.TrackHeaderOf(MabClip.SectionKeyframeRotation) is { } keyed
                    && clip.KeyframedBones().Count > 0)
                {
                    tally.Compare("keyframe rotations", clip, MabClip.SectionKeyframeRotation, path,
                        () => MabEncoder.KeyframeRotations(
                            clip.KeyframedBones(), clip.KeyframeTracks(), keyed.LastFrame, keyed.Rate));
                }
            }
        }
        return tally;
    }

    private sealed class Tally
    {
        private readonly Dictionary<string, int> _seen = [];
        private readonly Dictionary<string, int> _missed = [];

        public List<string> Samples { get; } = [];

        /// <summary>Clips holding a triple that is not a rotation, so nothing can write them back.</summary>
        public int Unencodable { get; private set; }

        public int Seen(string what) => _seen.GetValueOrDefault(what);

        public int Missed(string what) => _missed.GetValueOrDefault(what);

        public void Compare(string what, MabClip clip, int slot, string path, Func<byte[]> build)
        {
            if (clip.Section(slot) is not { } original)
            {
                return;
            }

            byte[] produced;
            try
            {
                produced = build();
            }
            catch (InvalidDataException)
            {
                Unencodable++;
                return;
            }

            _seen[what] = Seen(what) + 1;
            if (produced.Length <= original.Length
                && produced.AsSpan().SequenceEqual(original.AsSpan(0, produced.Length)))
            {
                return;
            }

            _missed[what] = Missed(what) + 1;
            if (Samples.Count < 5)
            {
                int at = Fc2Corpus.FirstDifference(
                    original.AsSpan(0, Math.Min(original.Length, produced.Length)), produced);
                Samples.Add($"{what} {Path.GetFileName(path)}: {produced.Length} bytes vs {original.Length} span, first differs at {at}");
            }
        }
    }
}
