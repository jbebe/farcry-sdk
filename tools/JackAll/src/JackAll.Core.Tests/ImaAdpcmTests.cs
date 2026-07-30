using JackAll.Tools.Format;

namespace JackAll.Core.Tests;

/// <summary>
/// <see cref="ImaAdpcm"/> is a byte-for-byte port of Dunia.dll's real decoder functions (traced live
/// via GhidraMCP - see its remarks), so these tests lean on two kinds of evidence: synthetic streams
/// whose output is predictable from the algorithm itself, and the real `FlatCopy` audio inside
/// Fixtures/Spk/004e1ccc_1644b214.spk (one mono record, one stereo - the same two files whose header
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
        byte[] stream = header.Concat(new byte[16]).ToArray(); // sixteen 0x00 bytes -> 32 zero nibbles

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);

        Assert.Equal(1, decoded.Channels);
        Assert.All(decoded.Samples, s => Assert.Equal(0, s));
    }

    [Fact]
    public void Mono_decode_produces_two_samples_per_input_byte()
    {
        byte[] header = BuildHeader(stereo: false);
        byte[] body = [0x12, 0x34, 0x56];
        byte[] stream = header.Concat(body).ToArray();

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);

        Assert.Equal(1, decoded.Channels);
        Assert.Equal(body.Length * 2, decoded.Samples.Length);
    }

    [Fact]
    public void Stereo_decode_produces_one_interleaved_LR_frame_per_input_byte()
    {
        byte[] header = BuildHeader(stereo: true);
        byte[] body = [0x12, 0x34, 0x56, 0x78];
        byte[] stream = header.Concat(body).ToArray();

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);

        Assert.Equal(2, decoded.Channels);
        Assert.Equal(body.Length * 2, decoded.Samples.Length); // L,R per byte = 2 samples per byte
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Decodes_the_real_mono_FlatCopy_record_end_to_end()
    {
        string path = "Fixtures/Spk/004e1ccc_1644b214.spk";
        if (!File.Exists(path)) return; // no-op if the fixture wasn't restored, matching SpkPackageTests' convention

        SpkPackage package = SpkPackage.Parse(File.ReadAllBytes(path));
        SpkRecord mono = package.Records.Single(r => r.Id == 0x004e1cba);

        Assert.NotNull(mono.FlatCopyAudioStream);
        Assert.Equal(ImaAdpcm.ExpectedVersion, mono.FlatCopyAudioStream![0]);
        Assert.Equal(0, mono.FlatCopyAudioStream[0x0c]); // mono flag

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(mono.FlatCopyAudioStream);

        Assert.Equal(1, decoded.Channels);
        Assert.Equal((mono.FlatCopyAudioStream.Length - ImaAdpcm.HeaderSize) * 2, decoded.Samples.Length);
        Assert.Contains(decoded.Samples, s => s != 0); // real audio, not a silent/degenerate stream
    }

    [Fact]
    public void Encode_then_decode_produces_the_correct_shape_and_a_valid_header()
    {
        short[] mono = BuildSineWave(frequency: 440, sampleRate: 8000, seconds: 0.1, amplitude: 12000);

        byte[] stream = ImaAdpcm.Encode(mono, channels: 1);
        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);

        Assert.Equal(ImaAdpcm.ExpectedVersion, stream[0]);
        Assert.Equal(0, stream[0x0c]); // mono flag
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
        Assert.NotEqual(0, stream[0x0c]); // stereo flag

        ImaAdpcm.DecodedAudio roundTripped = ImaAdpcm.Decode(stream);

        Assert.Equal(2, roundTripped.Channels);
        AssertClose(interleaved, roundTripped.Samples, maxRmsError: 600);
    }

    [Fact]
    public void Encoding_an_odd_number_of_mono_samples_still_produces_a_whole_number_of_bytes()
    {
        short[] samples = BuildSineWave(frequency: 440, sampleRate: 8000, seconds: 0.1, amplitude: 12000);
        short[] odd = samples[..(samples.Length - 1)]; // force an odd count
        Assert.True(odd.Length % 2 == 1);

        byte[] stream = ImaAdpcm.Encode(odd, channels: 1);

        // one extra (padding) sample decodes back out - the format has no separate sample-count field,
        // so a consumer already has to tolerate this same one-sample ambiguity on real files.
        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(stream);
        Assert.Equal(odd.Length + 1, decoded.Samples.Length);
    }

    [Fact]
    [Trait("Category", "RequiresFixture")]
    public void Re_encoding_a_real_records_decoded_audio_stays_close_to_the_original()
    {
        string path = "Fixtures/Spk/004e1ccc_1644b214.spk";
        if (!File.Exists(path)) return;

        SpkPackage package = SpkPackage.Parse(File.ReadAllBytes(path));
        SpkRecord mono = package.Records.Single(r => r.Id == 0x004e1cba);
        ImaAdpcm.DecodedAudio original = ImaAdpcm.Decode(mono.FlatCopyAudioStream!);

        ImaAdpcm.DecodedAudio roundTripped = ImaAdpcm.Decode(ImaAdpcm.Encode(original.Samples, original.Channels));

        AssertClose(original.Samples, roundTripped.Samples, maxRmsError: 600);
    }

    private static short[] BuildSineWave(double frequency, int sampleRate, double seconds, short amplitude)
    {
        int count = (int)(sampleRate * seconds);
        var samples = new short[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * frequency * i / sampleRate));
        }

        return samples;
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
