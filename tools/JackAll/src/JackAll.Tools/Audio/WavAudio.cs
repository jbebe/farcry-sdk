using System.Buffers.Binary;
using JackAll.Core.Format;

namespace JackAll.Tools.Audio;

/// <summary>Reads and writes 16-bit PCM `RIFF`/`WAVE` files - used to turn <see cref="ImaAdpcm"/>'s
/// decoded output into something any media player can open (<see cref="Write"/>), and to read back a
/// PCM file ffmpeg transcoded a replacement audio import to before <see cref="ImaAdpcm.Encode"/> runs
/// on it (<see cref="ReadPcm16"/>).</summary>
public static class WavAudio
{
    private const int BitsPerSample = 16;
    private const short PcmFormatTag = 1;

    /// <summary>One chunk of a `RIFF`/`WAVE` file's PCM audio, as read by <see cref="ReadPcm16"/>.</summary>
    public sealed class Pcm16Audio
    {
        public required short[] Samples { get; init; }
        public required int Channels { get; init; }
        public required int SampleRate { get; init; }
    }

    /// <summary>Reads a standard, uncompressed 16-bit PCM `RIFF`/`WAVE` file - exactly what
    /// `FfmpegAudio.TranscodeToPcmWavAsync` produces. Throws for anything else (a compressed format
    /// tag, a bit depth other than 16, or a file missing a `fmt `/`data` chunk).</summary>
    public static Pcm16Audio ReadPcm16(byte[] wav)
    {
        if (wav.Length < 12 || !ByteCursor.Matches(wav, 0, "RIFF"u8) || !ByteCursor.Matches(wav, 8, "WAVE"u8))
        {
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        }

        int channels = 0, sampleRate = 0, bitsPerSample = 0;
        short[]? samples = null;

        int pos = 12;
        while (pos + 8 <= wav.Length)
        {
            int bodyStart = pos + 8;
            int size = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(pos + 4));
            if (bodyStart + size > wav.Length)
            {
                throw new InvalidDataException($"Truncated chunk at offset 0x{pos:X}.");
            }

            if (ByteCursor.Matches(wav, pos, "fmt "u8))
            {
                short formatTag = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(bodyStart));
                if (formatTag != PcmFormatTag)
                {
                    throw new InvalidDataException($"Only uncompressed PCM is supported (format tag {formatTag}).");
                }

                channels = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(bodyStart + 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(bodyStart + 4));
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(bodyStart + 14));
            }
            else if (ByteCursor.Matches(wav, pos, "data"u8))
            {
                if (bitsPerSample != BitsPerSample)
                {
                    throw new InvalidDataException($"Only {BitsPerSample}-bit PCM is supported (got {bitsPerSample}-bit).");
                }

                samples = new short[size / sizeof(short)];
                for (int i = 0; i < samples.Length; i++)
                {
                    samples[i] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(bodyStart + i * sizeof(short)));
                }
            }

            pos = bodyStart + size + (size % 2); // chunks are word-aligned
        }

        if (samples is null)
        {
            throw new InvalidDataException("No \"data\" chunk found.");
        }

        return new Pcm16Audio { Samples = samples, Channels = channels, SampleRate = sampleRate };
    }

    public static byte[] Write(short[] samples, int channels, int sampleRate)
    {
        int dataSize = samples.Length * sizeof(short);
        int blockAlign = channels * BitsPerSample / 8;
        int byteRate = sampleRate * blockAlign;

        using var stream = new MemoryStream(44 + dataSize);
        using var w = new BinaryWriter(stream);

        w.Write("RIFF"u8);
        w.Write(36 + dataSize);
        w.Write("WAVE"u8);

        w.Write("fmt "u8);
        w.Write(16); // fmt chunk size
        w.Write((short)1); // PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)BitsPerSample);

        w.Write("data"u8);
        w.Write(dataSize);
        foreach (short sample in samples)
        {
            w.Write(sample);
        }

        return stream.ToArray();
    }
}
