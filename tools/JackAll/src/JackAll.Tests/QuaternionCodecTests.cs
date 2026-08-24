using System.Buffers.Binary;
using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// The smallest-three quaternion codec, held to reproducing the words it decoded.
/// </summary>
/// <remarks>
/// Every rotation in every shipped clip goes through this, so it is the foundation the clip writer
/// stands on - if the quantiser is not an identity on real data, nothing built above it can return
/// a file. Measured rather than assumed: the number that comes out is the mismatch count, and a
/// non-canonical encoding would show up here rather than as a limb pointing the wrong way.
/// </remarks>
public sealed class QuaternionCodecTests
{
    // Measures a rate, so a corpus of no files divides by zero rather than no-opping.
    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Every_shipped_rotation_packs_back_to_the_words_it_came_from()
    {
        long rotations = 0;
        long mismatched = 0;
        long drifted = 0;
        List<string> samples = [];

        foreach (string path in Fc2Corpus.Find(".mab"))
        {
            MabFile bank = MabFile.Parse(File.ReadAllBytes(path));
            foreach (MabClip clip in bank.Clips())
            {
                foreach (int section in (int[])[MabClip.SectionConstantRotation, MabClip.SectionRootRotation])
                {
                    if (clip.Section(section) is not { } block)
                    {
                        continue;
                    }

                    // Walk the whole section as packed triples; whatever the framing around them,
                    // each six bytes either decodes to a rotation or does not.
                    for (int at = MabClip.TrackHeader; at + MabClip.QuatBytes <= block.Length; at += MabClip.QuatBytes)
                    {
                        if (MabClip.ReadQuaternion(block, at) is not { } rotation)
                        {
                            continue;
                        }

                        rotations++;
                        (ushort first, ushort second, short third) = MabClip.PackQuaternion(rotation);
                        if (first == BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at))
                            && second == BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at + 2))
                            && third == BinaryPrimitives.ReadInt16LittleEndian(block.AsSpan(at + 4)))
                        {
                            continue;
                        }

                        mismatched++;

                        // A different encoding is only acceptable if it means the same rotation.
                        // Compared as a rotation, not componentwise: negating all four components
                        // is the same orientation, and dropping a negative component forces exactly
                        // that negation.
                        float[]? again = MabClip.UnpackQuaternion(first, second, third);
                        double dot = again is null
                            ? 0.0
                            : Math.Abs(Enumerable.Range(0, 4).Sum(i => (double)again[i] * rotation[i]));
                        if (dot < 0.9999 && samples.Count < 4)
                        {
                            samples.Add($"{Path.GetFileName(path)}+{at}: |dot| {dot:F6}");
                        }
                        drifted += dot < 0.9999 ? 1 : 0;
                    }
                }
            }
        }

        Assert.True(rotations > 0 || !Fc2Corpus.Present, "No rotation was examined.");

        // Every rotation has to come back meaning the same thing.
        Assert.True(
            drifted == 0,
            $"{drifted} of {rotations} rotations changed when repacked:{Environment.NewLine}"
            + string.Join(Environment.NewLine, samples));

        // Most come back bit-identical too. The rest cannot: they were authored on an exact tie -
        // a quarter turn, or an even diagonal putting all four components at 1/2 - and quantising
        // breaks that tie asymmetrically, so which component the original dropped is no longer
        // visible in what was stored. They re-encode to a different, equally valid triple.
        double exact = (rotations - mismatched) / (double)rotations;
        Assert.True(
            exact > 0.999,
            $"{rotations - mismatched}/{rotations} ({exact:P3}) packed back bit-identically; "
            + "expected better than 99.9%.");
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_corpus_was_actually_found()
        => Assert.True(Fc2Corpus.Find(".mab").Any(), Fc2Corpus.MissingMessage(".mab"));
}
