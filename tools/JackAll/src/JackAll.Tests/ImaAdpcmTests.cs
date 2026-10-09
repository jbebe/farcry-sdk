using JackAll.Tools.Audio;
using JackAll.Tools.Spk;

namespace JackAll.Tests;

/// <summary>
/// <see cref="ImaAdpcm"/> is a byte-for-byte port of Dunia.dll's real decoder functions (traced live
/// via GhidraMCP - see its remarks), so these tests lean on two kinds of evidence: synthetic streams
/// whose output is predictable from the algorithm itself, and the real `FlatCopy` audio inside
/// <see cref="SpkPackageTests.WithAudio"/> (one mono record, one stereo - the same two files whose header
/// bytes were checked by hand against the decompile before this port was written).
/// </summary>
public class ImaAdpcmTests
{
    private static byte[] BuildHeader(bool stereo, short predictorA = 0, byte stepIndexA = 0, short predictorB = 0, byte stepIndexB = 0)
    {
        byte[] header = new byte[ImaAdpcm.HeaderSize];
        header[0] = ImaAdpcm.ExpectedVersion;
        header[0x0c] = (byte)(stereo ? 1 : 0);
        BitConverter.GetBytes(predictorA).CopyTo(header, 0x10);
        header[0x12] = stepIndexA;
        BitConverter.GetBytes(predictorB).CopyTo(header, 0x14);
        header[0x16] = stepIndexB;
        return header;
    }

    private static int RawBytes(int channels) => ImaAdpcm.RawFrames * channels * sizeof(short);

    [Fact]
    public void Decode_rejects_a_stream_shorter_than_the_header()
        => Assert.Throws<InvalidDataException>(() => ImaAdpcm.Decode(new byte[ImaAdpcm.HeaderSize - 1]));

    [Fact]
    public void Decode_rejects_a_version_byte_other_than_five()
    {
        byte[] header = BuildHeader(stereo: false);
        header[0] = 3;

        var ex = Assert.Throws<InvalidDataException>(() => ImaAdpcm.Decode(header));
        Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_all_zero_nibble_stream_at_the_smallest_step_decodes_to_silence()
    {
        // nibble 0: sign bit clear, magnitude bits clear -> diff = (step * 1) >> 3, which floors to 0
        // for the smallest step-table entry (7); step-index also stays clamped at 0 (index delta -1,
        // already at the floor) - so this is a fixed point, not a coincidence of the first sample only.
        byte[] header = BuildHeader(stereo: false, predictorA: 0, stepIndexA: 0);
        // Ten zero raw frames, then sixteen 0x00 bytes -> 32 zero nibbles
        byte[] stream = header.Concat(new byte[RawBytes(1) + 16]).ToArray();

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);

        Assert.Equal(1, decoded.Channels);
        Assert.All(decoded.Samples, s => Assert.Equal(0, s));
    }

    [Fact]
    public void Mono_decode_yields_the_raw_frames_then_two_samples_per_nibble_byte()
    {
        short[] raw = [1, -2, 3, -4, 5, -6, 7, -8, 9, -10];
        byte[] body = [0x12, 0x34, 0x56];
        byte[] stream = [.. BuildHeader(stereo: false), .. raw.SelectMany(BitConverter.GetBytes), .. body];

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);

        Assert.Equal(1, decoded.Channels);
        Assert.Equal(raw, decoded.Samples[..ImaAdpcm.RawFrames]);
        Assert.Equal(ImaAdpcm.RawFrames + body.Length * 2, decoded.Samples.Length);
    }

    [Fact]
    public void Stereo_decode_yields_the_raw_frames_then_one_interleaved_LR_frame_per_nibble_byte()
    {
        byte[] body = [0x12, 0x34, 0x56, 0x78];
        byte[] stream = BuildHeader(stereo: true).Concat(new byte[RawBytes(2)]).Concat(body).ToArray();

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);

        Assert.Equal(2, decoded.Channels);
        Assert.Equal((ImaAdpcm.RawFrames + body.Length) * 2, decoded.Samples.Length);
    }

    [Fact]
    public void A_mono_odd_sample_plays_after_the_nibbles()
    {
        byte[] header = BuildHeader(stereo: false);
        header[0x18] = 1;
        BitConverter.GetBytes((short)1234).CopyTo(header, 0x1a);
        byte[] stream = header.Concat(new byte[RawBytes(1) + 4]).ToArray();

        short[] samples = ImaAdpcm.Decode(stream).Samples;

        Assert.Equal(ImaAdpcm.RawFrames + 8 + 1, samples.Length);
        Assert.Equal(1234, samples[^1]);
    }

    [Fact]
    public void Decodes_the_real_mono_FlatCopy_record_end_to_end()
    {
        if (Fixture.Read(SpkPackageTests.WithAudio) is not { } bank) return;

        SpkPackage package = SpkPackage.Parse(bank);
        SpkRecord mono = package.Records.Single(r => r.Id == 0x004e1cba);

        Assert.NotNull(mono.FlatCopyAudioStream);
        Assert.Equal(ImaAdpcm.ExpectedVersion, mono.FlatCopyAudioStream![0]);
        // Mono flag
        Assert.Equal(0, mono.FlatCopyAudioStream[0x0c]);

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(mono.FlatCopyAudioStream);

        Assert.Equal(1, decoded.Channels);
        Assert.Equal(ImaAdpcm.FrameCount(mono.FlatCopyAudioStream), decoded.Samples.Length);
        // Real audio, not a silent/degenerate stream
        Assert.Contains(decoded.Samples, s => s != 0);
    }

    /// <summary>Banks whose samples' declared frame counts the stream layout must reproduce; the second
    /// carries odd-sample streams.</summary>
    public static TheoryData<string> DeclaringBanks => new() { SpkPackageTests.WithAudio, "Spk/004565a3.spk" };

    [Theory]
    [MemberData(nameof(DeclaringBanks))]
    public void The_frame_count_matches_what_every_retail_sample_declares(string fixture)
    {
        if (Fixture.Read(fixture) is not { } bytes) return;

        SpkBank bank = SpkBank.Parse(bytes);
        var samples = bank.Records.Where(r => r.Layout == SpkLayout.Sample && bank.Find(r.Word(SpkLayout.SampleAudio)) is { IsAudio: true, Data: [ImaAdpcm.ExpectedVersion, ..] }).ToList();

        Assert.NotEmpty(samples);
        Assert.All(samples, sample =>
        {
            uint declared = Math.Max(sample.Word(SpkLayout.SampleOneShotFrames), sample.Word(SpkLayout.SampleLoopFrames));
            Assert.Equal(declared, ImaAdpcm.FrameCount(bank.Find(sample.Word(SpkLayout.SampleAudio))!.Data));
        });
    }

    [Fact]
    public void Encode_then_decode_produces_the_correct_shape_and_a_valid_header()
    {
        short[] mono = BuildSineWave(frequency: 440, sampleRate: 8000, seconds: 0.1, amplitude: 12000);

        byte[] stream = ImaAdpcm.Encode(mono, channels: 1);
        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);

        Assert.Equal(ImaAdpcm.ExpectedVersion, stream[0]);
        // Mono flag
        Assert.Equal(0, stream[0x0c]);
        Assert.Equal(1, decoded.Channels);
        Assert.Equal(mono.Length, decoded.Samples.Length);
    }

    [Fact]
    public void Encode_then_decode_stays_close_to_the_original_mono_waveform()
    {
        short[] original = BuildSineWave(frequency: 440, sampleRate: 8000, seconds: 0.5, amplitude: 12000);

        ImaAdpcm.DecodedAudio roundTripped = ImaAdpcm.Decode(ImaAdpcm.Encode(original, channels: 1));

        AssertClose(original, roundTripped.Samples, maxRmsError: 600);
    }

    [Fact]
    public void Encode_then_decode_stays_close_to_the_original_stereo_waveform()
    {
        short[] left = BuildSineWave(frequency: 440, sampleRate: 8000, seconds: 0.3, amplitude: 12000);
        short[] right = BuildSineWave(frequency: 660, sampleRate: 8000, seconds: 0.3, amplitude: 8000);
        short[] interleaved = new short[left.Length * 2];
        for (int i = 0; i < left.Length; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[i * 2 + 1] = right[i];
        }

        byte[] stream = ImaAdpcm.Encode(interleaved, channels: 2);
        // Stereo flag
        Assert.NotEqual(0, stream[0x0c]);

        ImaAdpcm.DecodedAudio roundTripped = ImaAdpcm.Decode(stream);

        Assert.Equal(2, roundTripped.Channels);
        AssertClose(interleaved, roundTripped.Samples, maxRmsError: 600);
    }

    [Fact]
    public void The_first_frames_are_stored_raw_so_a_loud_start_or_a_loop_restart_has_no_slew()
    {
        // Starts at full amplitude, where a predictor slewing up from zero would miss by thousands.
        short[] samples = BuildSineWave(frequency: 400, sampleRate: 8000, seconds: 0.25, amplitude: 12000, phase: Math.PI / 2);

        byte[] stream = ImaAdpcm.Encode(samples, channels: 1);
        short[] decoded = ImaAdpcm.Decode(stream).Samples;

        Assert.Equal(ImaAdpcm.RawFrames, stream[0x0e]);
        Assert.Equal(samples[..ImaAdpcm.RawFrames], decoded[..ImaAdpcm.RawFrames]);
        int steadyState = MaxError(samples, decoded, 32, samples.Length);
        Assert.True(MaxError(samples, decoded, ImaAdpcm.RawFrames, 32) <= steadyState * 3 / 2);
    }

    [Fact]
    public void An_odd_number_of_mono_samples_round_trips_through_the_odd_sample()
    {
        short[] samples = BuildSineWave(frequency: 440, sampleRate: 8000, seconds: 0.1, amplitude: 12000);
        short[] odd = samples.Length % 2 == 0 ? samples[..^1] : samples;
        Assert.True((odd.Length - ImaAdpcm.RawFrames) % 2 == 1);

        byte[] stream = ImaAdpcm.Encode(odd, channels: 1);
        short[] decoded = ImaAdpcm.Decode(stream).Samples;

        Assert.Equal(1, stream[0x18]);
        Assert.Equal(odd.Length, decoded.Length);
        Assert.Equal(odd.Length, ImaAdpcm.FrameCount(stream));
        Assert.Equal(odd[^1], decoded[^1]);
    }

    [Fact]
    public void Re_encoding_a_real_records_decoded_audio_stays_close_to_the_original()
    {
        if (Fixture.Read(SpkPackageTests.WithAudio) is not { } bank) return;

        SpkPackage package = SpkPackage.Parse(bank);
        SpkRecord mono = package.Records.Single(r => r.Id == 0x004e1cba);
        ImaAdpcm.DecodedAudio original = ImaAdpcm.Decode(mono.FlatCopyAudioStream!);

        ImaAdpcm.DecodedAudio roundTripped = ImaAdpcm.Decode(ImaAdpcm.Encode(original.Samples, original.Channels));

        AssertClose(original.Samples, roundTripped.Samples, maxRmsError: 600);
    }

    private static short[] BuildSineWave(double frequency, int sampleRate, double seconds, short amplitude, double phase = 0)
    {
        int count = (int)(sampleRate * seconds);
        var samples = new short[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * frequency * i / sampleRate + phase));
        }

        return samples;
    }

    private static int MaxError(short[] expected, short[] actual, int from, int to)
    {
        int max = 0;
        for (int i = from; i < to; i++)
        {
            max = Math.Max(max, Math.Abs(expected[i] - actual[i]));
        }

        return max;
    }

    /// <summary>IMA-ADPCM is lossy by design (4 bits per sample) and, being a running predictor with a
    /// bounded per-sample slew rate, can't instantly jump from a standing start (predictor/step both at
    /// their minimum) to a signal that's already near full amplitude on its very first sample - so a
    /// worst-single-sample bound isn't the right check right at the start of a synthetic test tone.
    /// Root-mean-square error over the whole signal is what real-world ADPCM quality is judged by, and
    /// isn't dominated by that one unavoidable startup transient.</summary>
    private static void AssertClose(short[] expected, short[] actual, double maxRmsError)
    {
        Assert.Equal(expected.Length, actual.Length);

        double sumSquaredError = 0;
        for (int i = 0; i < expected.Length; i++)
        {
            double error = expected[i] - actual[i];
            sumSquaredError += error * error;
        }

        double rms = Math.Sqrt(sumSquaredError / expected.Length);
        Assert.True(rms <= maxRmsError, $"RMS error {rms:0.#} exceeds {maxRmsError}");
    }
}
