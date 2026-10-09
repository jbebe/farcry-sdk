using System.Buffers.Binary;
using System.Runtime.InteropServices;
using JackAll.Tools.Sbao;
using JackAll.Tools.Spk;

namespace JackAll.Tools.Audio;

/// <summary>
/// Decodes (and encodes) Far Cry 2's DARE `TImaAdpcm` streams - the codec behind the compressed audio
/// bytes in an `.spk` `FlatCopy` record's payload (see <see cref="SpkPackage"/>) and, per the same trace, the
/// short-SFX `.sbao` sub-type as well (see <see cref="SbaoAudio"/>'s remarks on that still-unconfirmed
/// outer envelope).
///
/// Traced live via GhidraMCP against `Dunia.dll`. Found by byte-searching the binary directly (not by
/// name/string search - this code has no nearby strings) for the two canonical IMA-ADPCM reference
/// tables, which exist verbatim, back to back, in `.rdata`: the 16-entry step-index table and the
/// 89-entry step-size table, both stored as `int32` arrays. `get_xrefs_to` on the index table led
/// straight to the real decoder: a mono block decoder and a 2-channel "nibbles separated by channel"
/// variant, both textbook IMA-ADPCM, dispatched by a channel-flag switch whose caller reads the
/// 28-byte per-stream header parsed here before decoding.
///
/// This is the standard, publicly documented algorithm - not a customized dialect, and a different,
/// unrelated codec family from Ubisoft's own older in-house "Ubi Sound Tools" ADPCM (the one the
/// third-party tool Ubitunedec decodes) despite both being "an ADPCM."
///
/// Verified against real `.spk` `FlatCopy` payloads: decoding runs to completion consuming 100% of the
/// post-header bytes with no errors, on both a real mono and a real stereo sample, each producing
/// audio-plausible output (a wide, roughly zero-centered dynamic range) written out as playable .wav.
/// </summary>
public static class ImaAdpcm
{
    /// <summary>Expected value of the header's version byte (offset 0x00). Anything else means this
    /// isn't (or isn't a supported version of) a `TImaAdpcm` stream.</summary>
    public const byte ExpectedVersion = 5;

    /// <summary>Size of the per-stream header before the raw frames begin.</summary>
    public const int HeaderSize = 0x1c;

    /// <summary>Frames stored as raw 16-bit PCM between the header and the nibbles. The engine hard-codes
    /// the count; the header's byte at 0x0E only records it.</summary>
    public const int RawFrames = 10;

    private const int ChannelFlagOffset = 0x0c;
    private const int RawFrameCountOffset = 0x0e;
    private const int PredictorAOffset = 0x10;
    private const int StepIndexAOffset = 0x12;
    private const int PredictorBOffset = 0x14;
    private const int StepIndexBOffset = 0x16;

    // A mono stream with an odd sample count after the raw frames sets this flag and carries the last
    // sample here, played after the nibbles.
    private const int OddFlagOffset = 0x18;
    private const int OddSampleOffset = 0x1a;

    private const int MaxStepIndex = 0x58; // 88 - the last valid index into StepTable (89 entries)

    /// <summary>The canonical IMA-ADPCM step-index adjustment table - confirmed byte-for-byte at
    /// Dunia.dll VA 0x10ee3928 (stored there as int32, not the more common int16).</summary>
    private static readonly int[] IndexTable = [-1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8];

    /// <summary>The canonical IMA-ADPCM step-size table - confirmed byte-for-byte at Dunia.dll VA
    /// 0x10ee3968, immediately after <see cref="IndexTable"/>.</summary>
    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73,
        80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494,
        544, 598, 658, 724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499,
        2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487,
        12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    ];

    /// <summary>One decoded `TImaAdpcm` stream: interleaved 16-bit PCM samples (mono, or `L,R,L,R,...`
    /// for stereo) plus the channel count actually decoded.</summary>
    public sealed class DecodedAudio
    {
        public required short[] Samples { get; init; }
        public required int Channels { get; init; }
    }

    /// <summary>The channel count a stream's header declares.</summary>
    public static int Channels(ReadOnlySpan<byte> stream) => stream[ChannelFlagOffset] != 0 ? 2 : 1;

    /// <summary>The frames the engine plays from a stream: the raw frames, the nibbles and the odd sample.</summary>
    public static long FrameCount(ReadOnlySpan<byte> stream)
    {
        int channels = Channels(stream);
        long nibbleBytes = Math.Max(0, stream.Length - RawEnd(channels));
        return RawFrames + nibbleBytes * 2 / channels + (HasOddSample(stream) ? 1 : 0);
    }

    private static int RawEnd(int channels) => HeaderSize + RawFrames * channels * sizeof(short);

    private static bool HasOddSample(ReadOnlySpan<byte> stream) => Channels(stream) == 1 && stream[OddFlagOffset] == 1;

    /// <summary>
    /// Parses the 28-byte `TImaAdpcm` stream header, then returns the raw frames, the decoded nibbles and
    /// the odd sample. Throws if the version byte isn't <see cref="ExpectedVersion"/> - matching the
    /// engine's own "IMA-ADPCM version seems to be too old" rejection - or if the stream is too short to
    /// hold a header at all.
    /// </summary>
    public static DecodedAudio Decode(byte[] stream)
    {
        if (stream.Length < HeaderSize)
        {
            throw new InvalidDataException(
                $"TImaAdpcm stream is only {stream.Length} bytes, too small for the {HeaderSize}-byte header.");
        }

        byte version = stream[0];
        if (version != ExpectedVersion)
        {
            throw new InvalidDataException(
                $"TImaAdpcm: IMA-ADPCM version seems to be too old (got {version}, expected {ExpectedVersion}).");
        }

        bool stereo = Channels(stream) == 2;
        int rawEnd = Math.Min(stream.Length, RawEnd(stereo ? 2 : 1));
        short[] raw = MemoryMarshal.Cast<byte, short>(stream.AsSpan(HeaderSize, (rawEnd - HeaderSize) & ~1)).ToArray();
        short predictorA = (short)BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(PredictorAOffset));
        int stepIndexA = stream[StepIndexAOffset];

        var body = stream.AsSpan(rawEnd);

        if (!stereo)
        {
            short[] odd = HasOddSample(stream) ? [BinaryPrimitives.ReadInt16LittleEndian(stream.AsSpan(OddSampleOffset))] : [];
            return new DecodedAudio { Samples = [.. raw, .. DecodeMono(body, predictorA, stepIndexA), .. odd], Channels = 1 };
        }

        short predictorB = (short)BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(PredictorBOffset));
        int stepIndexB = stream[StepIndexBOffset];
        return new DecodedAudio
        {
            Samples = [.. raw, .. DecodeStereoInterleaved(body, predictorA, stepIndexA, predictorB, stepIndexB)],
            Channels = 2,
        };
    }

    /// <summary>
    /// Encodes 16-bit PCM samples (mono, or interleaved `L,R,L,R,...` for stereo) into a `TImaAdpcm`
    /// stream laid out as retail ones are - header, <see cref="RawFrames"/> raw frames, packed nibbles -
    /// ready to drop straight into an `.spk` `FlatCopy` record's payload (after that record's unchanged
    /// 40-byte core). A clip shorter than the raw frames is padded with silence.
    /// </summary>
    public static byte[] Encode(short[] samples, int channels)
    {
        if (channels is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Only mono (1) or stereo (2) audio is supported.");
        }

        int rawSamples = RawFrames * channels;
        short[] padded = samples.Length >= rawSamples ? samples : [.. samples, .. new short[rawSamples - samples.Length]];
        ReadOnlySpan<short> raw = padded.AsSpan(0, rawSamples);
        ReadOnlySpan<short> rest = padded.AsSpan(rawSamples);
        bool odd = channels == 1 && rest.Length % 2 == 1;
        if (odd)
        {
            rest = rest[..^1];
        }

        (int predictorA, int stepIndexA) = PrimedState(raw, channels, channel: 0);
        (int predictorB, int stepIndexB) = channels == 2 ? PrimedState(raw, channels, channel: 1) : (0, 0);

        byte[] header = new byte[HeaderSize];
        header[0] = ExpectedVersion;
        header[ChannelFlagOffset] = (byte)(channels - 1);
        header[RawFrameCountOffset] = RawFrames;
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(PredictorAOffset), (short)predictorA);
        header[StepIndexAOffset] = (byte)stepIndexA;
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(PredictorBOffset), (short)predictorB);
        header[StepIndexBOffset] = (byte)stepIndexB;
        if (odd)
        {
            header[OddFlagOffset] = 1;
            BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(OddSampleOffset), padded[^1]);
        }

        byte[] body = channels == 2
            ? EncodeStereoInterleaved(rest, predictorA, stepIndexA, predictorB, stepIndexB)
            : EncodeMono(rest, predictorA, stepIndexA);

        return [.. header, .. MemoryMarshal.AsBytes(raw).ToArray(), .. body];
    }

    /// <summary>The state the nibbles start from: the last raw sample, and the step index the encoder
    /// reaches tracking one channel's raw frames.</summary>
    private static (int Predictor, int StepIndex) PrimedState(ReadOnlySpan<short> raw, int channels, int channel)
    {
        int predictor = raw[channel], stepIndex = 0, step = StepTable[0];
        for (int f = 1; f < RawFrames; f++)
        {
            EncodeNibble(raw[f * channels + channel], ref predictor, ref stepIndex, ref step);
        }

        return (raw[(RawFrames - 1) * channels + channel], stepIndex);
    }

    /// <summary>Mono block decode - byte-for-byte port of `Dunia.dll`'s `0x10a85150`: each input byte
    /// yields two output samples, high nibble first.</summary>
    private static short[] DecodeMono(ReadOnlySpan<byte> data, int predictor, int stepIndex)
    {
        var samples = new short[data.Length * 2];
        int step = StepTable[stepIndex];

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            (predictor, stepIndex, step) = DecodeNibble(b >> 4, predictor, stepIndex, step);
            samples[i * 2] = (short)predictor;
            (predictor, stepIndex, step) = DecodeNibble(b & 0xf, predictor, stepIndex, step);
            samples[i * 2 + 1] = (short)predictor;
        }

        return samples;
    }

    /// <summary>Stereo decode - byte-for-byte port of `Dunia.dll`'s `0x10a85240`: each input byte's
    /// high nibble is channel A's next sample and low nibble is channel B's, so the output is already
    /// interleaved `L,R,L,R,...` one frame per input byte ("4 bits separate" per-channel encoding).</summary>
    private static short[] DecodeStereoInterleaved(
        ReadOnlySpan<byte> data, int predictorA, int stepIndexA, int predictorB, int stepIndexB)
    {
        var samples = new short[data.Length * 2];
        int stepA = StepTable[stepIndexA];
        int stepB = StepTable[stepIndexB];

        for (int i = 0; i < data.Length; i++)
        {
            byte b = data[i];
            (predictorA, stepIndexA, stepA) = DecodeNibble(b >> 4, predictorA, stepIndexA, stepA);
            samples[i * 2] = (short)predictorA;
            (predictorB, stepIndexB, stepB) = DecodeNibble(b & 0xf, predictorB, stepIndexB, stepB);
            samples[i * 2 + 1] = (short)predictorB;
        }

        return samples;
    }

    /// <summary>Mono encode - the inverse of <see cref="DecodeMono"/>: two samples packed per output
    /// byte, high nibble first, so the sample count must be even.</summary>
    private static byte[] EncodeMono(ReadOnlySpan<short> samples, int predictor, int stepIndex)
    {
        int step = StepTable[stepIndex];
        var data = new byte[samples.Length / 2];

        for (int i = 0; i < data.Length; i++)
        {
            int hi = EncodeNibble(samples[i * 2], ref predictor, ref stepIndex, ref step);
            int lo = EncodeNibble(samples[i * 2 + 1], ref predictor, ref stepIndex, ref step);
            data[i] = (byte)((hi << 4) | lo);
        }

        return data;
    }

    /// <summary>Stereo encode - the inverse of <see cref="DecodeStereoInterleaved"/>: one input `L,R`
    /// frame packed per output byte (high nibble channel A, low nibble channel B). Requires an even
    /// number of samples (a whole number of `L,R` frames).</summary>
    private static byte[] EncodeStereoInterleaved(
        ReadOnlySpan<short> interleaved, int predictorA, int stepIndexA, int predictorB, int stepIndexB)
    {
        int frames = interleaved.Length / 2;
        int stepA = StepTable[stepIndexA];
        int stepB = StepTable[stepIndexB];
        var data = new byte[frames];

        for (int i = 0; i < frames; i++)
        {
            int hi = EncodeNibble(interleaved[i * 2], ref predictorA, ref stepIndexA, ref stepA);
            int lo = EncodeNibble(interleaved[i * 2 + 1], ref predictorB, ref stepIndexB, ref stepB);
            data[i] = (byte)((hi << 4) | lo);
        }

        return data;
    }

    /// <summary>Picks whichever of the 16 possible nibbles <see cref="DecodeNibble"/> would turn back
    /// into the value closest to <paramref name="sample"/>, and commits that nibble's resulting
    /// predictor/step-index/step - i.e. a brute-force search built directly on top of the decoder
    /// rather than a separately-derived formula, so encode and decode can never drift out of sync with
    /// each other over a long stream.</summary>
    private static int EncodeNibble(int sample, ref int predictor, ref int stepIndex, ref int step)
    {
        int bestNibble = 0, bestPredictor = predictor, bestStepIndex = stepIndex, bestStep = step;
        int bestError = int.MaxValue;

        for (int nibble = 0; nibble < 16; nibble++)
        {
            (int p, int si, int s) = DecodeNibble(nibble, predictor, stepIndex, step);
            int error = Math.Abs(p - sample);
            if (error < bestError)
            {
                bestError = error;
                bestNibble = nibble;
                bestPredictor = p;
                bestStepIndex = si;
                bestStep = s;
            }
        }

        predictor = bestPredictor;
        stepIndex = bestStepIndex;
        step = bestStep;
        return bestNibble;
    }

    /// <summary>The actual IMA-ADPCM arithmetic, shared by both channel layouts: adjust the predictor
    /// by this nibble's delta (clamped to 16 bits), then adjust the step-index for next time (clamped
    /// to the step table's valid range).</summary>
    private static (int Predictor, int StepIndex, int Step) DecodeNibble(int nibble, int predictor, int stepIndex, int step)
    {
        int diff = (step * (2 * (nibble & 7) + 1)) >> 3;
        if ((nibble & 8) != 0)
        {
            diff = -diff;
        }

        predictor = Math.Clamp(predictor + diff, short.MinValue, short.MaxValue);

        stepIndex = Math.Clamp(stepIndex + IndexTable[nibble & 0xf], 0, MaxStepIndex);
        return (predictor, stepIndex, StepTable[stepIndex]);
    }
}
