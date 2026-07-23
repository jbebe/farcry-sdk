namespace JackAll.Core.Format;

/// <summary>
/// Splits the Ogg-backed variant of a Dunia .sbao audio object (music, dialogue) into its
/// engine header and the embedded, fully valid Ogg Vorbis bitstream, and reassembles the two back
/// into a byte-identical .sbao.
/// </summary>
/// <remarks>
/// Layout confirmed by direct byte-analysis of shipped music .sbao files (see
/// research/sbao_format.md): a 40-byte header — byte-identical across files except a 16-byte asset
/// GUID at 0x08, which is not a content hash and does not need to be recomputed — followed
/// immediately by a complete Ogg Vorbis stream. The header's field at 0x04 is a little-endian u32
/// giving the payload offset (always 40 in retail files, but read rather than assumed, matching
/// sbao_tool.py).
///
/// The short-SFX .sbao variant (Ubisoft ADPCM, no Ogg bitstream) is a different, undocumented layout
/// and is deliberately not handled here — <see cref="Split"/> throws for it.
/// </remarks>
public static class SbaoAudio
{
    private const int HeaderOffsetField = 0x04;
    private const int MinScannable = 8;

    /// <summary>Splits raw .sbao bytes into the header (everything before the Ogg payload) and the Ogg payload.</summary>
    public static (byte[] Header, byte[] Ogg) Split(byte[] sbao)
    {
        int offset = FindOggOffset(sbao);
        return (sbao[..offset], sbao[offset..]);
    }

    /// <summary>Reassembles an .sbao file from a header (as produced by <see cref="Split"/>) and an Ogg payload.</summary>
    public static byte[] Combine(byte[] header, byte[] ogg)
    {
        byte[] result = new byte[header.Length + ogg.Length];
        header.CopyTo(result, 0);
        ogg.CopyTo(result, header.Length);
        return result;
    }

    /// <summary>
    /// Reads (sample rate, channels) from the Vorbis identification header of an Ogg bitstream, or
    /// null if <paramref name="ogg"/> doesn't parse as one. Used to confirm a replacement encode
    /// actually landed at Far Cry 2's required 48 kHz stereo before it's accepted.
    /// </summary>
    public static (int SampleRate, int Channels)? TryReadVorbisId(byte[] ogg)
    {
        // First Ogg page: 'OggS'(4) ver(1) type(1) granule(8) serial(4) seq(4) crc(4) nsegs(1) segtable(nsegs);
        // then the Vorbis ID packet: 0x01 'vorbis'(6) version(4) channels(1) sample_rate(4 LE) ...
        if (ogg.Length < 28 || !Matches(ogg, 0, "OggS"u8))
        {
            return null;
        }

        int nsegs = ogg[26];
        int packet = 27 + nsegs;
        if (packet + 16 > ogg.Length || ogg[packet] != 0x01 || !Matches(ogg, packet + 1, "vorbis"u8))
        {
            return null;
        }

        int channels = ogg[packet + 11];
        int sampleRate = (int)ReadU32(ogg, packet + 12);
        return (sampleRate, channels);
    }

    private static int FindOggOffset(byte[] data)
    {
        if (data.Length < MinScannable)
        {
            throw new InvalidDataException("File too small to be an .sbao.");
        }

        uint offset = ReadU32(data, HeaderOffsetField);
        if (offset > 0 && offset < data.Length - 4 && Matches(data, (int)offset, "OggS"u8))
        {
            return (int)offset;
        }

        int idx = IndexOf(data, "OggS"u8);
        if (idx < 0)
        {
            throw new InvalidDataException(
                "No Ogg bitstream found - not an Ogg-backed .sbao (short SFX .sbao use Ubi ADPCM codecs, unsupported here).");
        }
        return idx;
    }

    private static uint ReadU32(byte[] data, int offset)
        => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static bool Matches(byte[] data, int offset, ReadOnlySpan<byte> expected)
        => offset >= 0 && offset + expected.Length <= data.Length
           && data.AsSpan(offset, expected.Length).SequenceEqual(expected);

    private static int IndexOf(byte[] data, ReadOnlySpan<byte> pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }
        return -1;
    }
}
