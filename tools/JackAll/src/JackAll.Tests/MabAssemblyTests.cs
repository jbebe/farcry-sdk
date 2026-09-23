using JackAll.Tools.Mab;

namespace JackAll.Tests;

/// <summary>
/// A whole clip laid out from its sections, and required to match the bytes it was read from.
/// </summary>
/// <remarks>
/// <see cref="MabEncoderTests"/> proves each section's contents; this proves the framing around
/// them - the order they sit in, the 16-byte alignment, the zero separator before the chained clip,
/// and the offsets the table then has to carry.
/// <para>
/// The banks carry no event chunk, because that one is FCB and its length cannot be computed from
/// anything decoded - carrying it verbatim would carry its padding too and prove nothing about where
/// it starts.
/// </para>
/// </remarks>
public sealed class MabAssemblyTests
{
    [Theory]
    [InlineData(MabFixtures.Pistol, true)]
    [InlineData(MabFixtures.Tie, false)]
    public void A_clip_lays_out_where_it_was_read_from(string fixture, bool byteExact)
    {
        if (Fixture.Read(fixture) is not { } bytes)
        {
            return;
        }

        foreach (MabClip clip in MabFile.Parse(bytes).Clips())
        {
            (byte[] data, int[] offsets) = MabEncoder.Assemble(MabSections.Intrinsic(clip));

            Assert.Equal(clip.Sections, offsets);
            Assert.Equal(clip.Data.Length, data.Length);
            if (byteExact)
            {
                Fixture.AssertSameBytes(fixture, clip.Data, data);
            }
        }
    }
}
