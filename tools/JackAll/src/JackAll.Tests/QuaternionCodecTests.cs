using System.Buffers.Binary;
using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// The smallest-three quaternion codec, held to reproducing the words it decoded.
/// </summary>
/// <remarks>
/// Every rotation a clip holds goes through this, so it is the foundation the clip writer
/// stands on - if the quantiser is not an identity on real data, nothing built above it can return
/// a file. A non-canonical encoding would show up here rather than as a limb pointing the wrong way.
/// </remarks>
public sealed class QuaternionCodecTests
{
    /// <summary>
    /// Every rotation has to come back meaning the same thing, and all but the ties bit-identically
    /// too. A tie - a quarter turn, or an even diagonal putting all four components at 1/2 - breaks
    /// asymmetrically when quantised, so it re-encodes to a different, equally valid triple.
    /// </summary>
    [Theory]
    [InlineData(MabFixtures.Pistol, 0)]
    [InlineData(MabFixtures.Tie, 1)]
    public void Every_rotation_packs_back_to_the_words_it_came_from(string fixture, int ties)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        int mismatched = 0;
        foreach (MabClip clip in MabFile.Parse(bytes).Clips())
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

                    (ushort first, ushort second, short third) = MabClip.PackQuaternion(rotation);
                    if (first == BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at))
                        && second == BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(at + 2))
                        && third == BinaryPrimitives.ReadInt16LittleEndian(block.AsSpan(at + 4)))
                    {
                        continue;
                    }

                    mismatched++;

                    // Compared as a rotation, not componentwise: negating all four components is
                    // the same orientation, and dropping a negative component forces exactly that.
                    float[]? again = MabClip.UnpackQuaternion(first, second, third);
                    double dot = again is null
                        ? 0.0
                        : Math.Abs(Enumerable.Range(0, 4).Sum(i => (double)again[i] * rotation[i]));
                    Assert.True(dot >= 0.9999, $"{fixture}+{at}: |dot| {dot:F6}");
                }
            }
        }
        Assert.Equal(ties, mismatched);
    }
}
