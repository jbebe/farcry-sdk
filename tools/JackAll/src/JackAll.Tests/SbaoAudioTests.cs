using JackAll.Tools.Sbao;

namespace JackAll.Tests;

/// <summary>
/// Round-trip and Vorbis-ID checks run against real shipped Ogg-backed .sbao files (see
/// Fixtures/Sbao) - one mono/32kHz dialogue-style file and one stereo/48kHz file, giving coverage of
/// both channel layouts the engine actually ships. The header-layout edge cases (custom payload
/// offset, a wrong offset field falling back to a scan, no Ogg bitstream at all) aren't things any
/// real shipped file exhibits, so those still build a synthetic header + minimal Vorbis-ID-only Ogg
/// page by hand, matching the layout documented in research/sbao_format.md.
/// </summary>
public class SbaoAudioTests
{
    private const int HeaderSize = 40;

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void The_fixtures_were_actually_found() => Fixture.AssertPresent("Sbao/004b49fc.sbao", "Sbao/004e1b16.sbao");

    [Theory]
    [InlineData("Sbao/004b49fc.sbao")]
    [InlineData("Sbao/004e1b16.sbao")]
    public void Splitting_then_combining_a_real_shipped_sbao_reproduces_the_original_bytes(string path)
    {
        if (Fixture.Read(path) is not { } original) return;

        (byte[] header, byte[] ogg) = SbaoAudio.Split(original);

        Assert.Equal(HeaderSize, header.Length);
        Assert.Equal(original, SbaoAudio.Combine(header, ogg));
    }

    /// <summary>The rate and channels each fixture's Vorbis ID packet carries, confirmed by direct byte
    /// inspection.</summary>
    [Theory]
    [InlineData("Sbao/004b49fc.sbao", 32000, 1)]
    [InlineData("Sbao/004e1b16.sbao", 48000, 2)]
    public void TryReadVorbisId_reads_the_real_sample_rate_and_channels(string path, int sampleRate, int channels)
    {
        if (Fixture.Read(path) is not { } original) return;

        (_, byte[] ogg) = SbaoAudio.Split(original);

        var id = SbaoAudio.TryReadVorbisId(ogg);

        Assert.NotNull(id);
        Assert.Equal(sampleRate, id!.Value.SampleRate);
        Assert.Equal(channels, id.Value.Channels);
    }

    private static byte[] BuildHeader(uint payloadOffset = HeaderSize)
    {
        byte[] header = new byte[HeaderSize];
        // Constant magic
        header[0] = 0x02; header[1] = 0x1F; header[2] = 0x00; header[3] = 0x10;
        BitConverter.GetBytes(payloadOffset).CopyTo(header, 4);
        // 0x08..0x18 asset GUID: arbitrary, it's per-asset and not content-derived.
        for (int i = 0x08; i < 0x18; i++) header[i] = (byte)(i * 7);
        // 00 00 00 50
        header[0x20 + 3] = 0x50;
        // Channel count constant
        header[0x24] = 0x02;
        return header;
    }

    /// <summary>A minimal but structurally valid single Ogg page carrying a Vorbis identification packet.</summary>
    private static byte[] BuildOggVorbisId(int sampleRate, int channels)
    {
        byte[] packet = new byte[30];
        packet[0] = 0x01;
        "vorbis"u8.CopyTo(packet.AsSpan(1));
        // bytes 7..10: vorbis_version (4, LE) = 0
        packet[11] = (byte)channels;
        BitConverter.GetBytes(sampleRate).CopyTo(packet, 12);

        byte[] page = new byte[27 + 1 + packet.Length];
        "OggS"u8.CopyTo(page.AsSpan(0));
        // Stream_structure_version
        page[4] = 0;
        // Header_type: first page of stream
        page[5] = 0x02;
        // bytes 6..25: granule(8) + serial(4) + seq(4) + crc(4) = 0, fine for a test fixture
        // Nsegs
        page[26] = 1;
        // Segment table: one segment holding the whole packet
        page[27] = (byte)packet.Length;
        packet.CopyTo(page, 28);
        return page;
    }

    [Fact]
    public void Split_uses_the_header_offset_field_rather_than_assuming_forty()
    {
        // A payload offset different from the retail-constant 40 still has to resolve correctly.
        const int customOffset = 48;
        byte[] header = new byte[customOffset];
        // Reuse the 40-byte fixed portion
        BuildHeader().CopyTo(header, 0);
        BitConverter.GetBytes((uint)customOffset).CopyTo(header, 4);

        byte[] ogg = BuildOggVorbisId(48000, 2);
        byte[] original = SbaoAudio.Combine(header, ogg);

        (byte[] splitHeader, byte[] splitOgg) = SbaoAudio.Split(original);

        Assert.Equal(header, splitHeader);
        Assert.Equal(ogg, splitOgg);
    }

    [Fact]
    public void Split_falls_back_to_scanning_for_OggS_when_the_offset_field_is_wrong()
    {
        // Deliberately bogus
        byte[] header = BuildHeader(payloadOffset: 0);
        byte[] ogg = BuildOggVorbisId(48000, 2);
        byte[] original = SbaoAudio.Combine(header, ogg);

        (byte[] splitHeader, byte[] splitOgg) = SbaoAudio.Split(original);

        Assert.Equal(header, splitHeader);
        Assert.Equal(ogg, splitOgg);
    }

    [Fact]
    public void Split_rejects_a_file_with_no_Ogg_bitstream_anywhere()
        => Assert.Throws<InvalidDataException>(() => SbaoAudio.Split(new byte[64]));

    [Fact]
    public void TryReadVorbisId_returns_null_for_non_Ogg_data()
        => Assert.Null(SbaoAudio.TryReadVorbisId(new byte[64]));
}
