using System.Buffers.Binary;
using JackAll.Core.Format;

namespace JackAll.Core.Tests;

public class WavAudioTests
{
    [Fact]
    public void Write_produces_a_well_formed_RIFF_WAVE_header()
    {
        short[] samples = [1, -1, 32767, -32768];
        byte[] wav = WavAudio.Write(samples, channels: 2, sampleRate: 44100);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(wav, 36, 4));

        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20))); // PCM format tag
        Assert.Equal(2, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22))); // channels
        Assert.Equal(44100, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24))); // sample rate
        Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34))); // bits per sample

        int dataSize = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40));
        Assert.Equal(samples.Length * sizeof(short), dataSize);
        Assert.Equal(44 + dataSize, wav.Length);

        for (int i = 0; i < samples.Length; i++)
        {
            Assert.Equal(samples[i], BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + i * 2)));
        }
    }

    [Fact]
    public void ReadPcm16_reads_back_exactly_what_Write_wrote()
    {
        short[] samples = [1, -1, 32767, -32768, 0, 12345];
        byte[] wav = WavAudio.Write(samples, channels: 2, sampleRate: 22050);

        WavAudio.Pcm16Audio read = WavAudio.ReadPcm16(wav);

        Assert.Equal(samples, read.Samples);
        Assert.Equal(2, read.Channels);
        Assert.Equal(22050, read.SampleRate);
    }

    [Fact]
    public void ReadPcm16_rejects_a_non_RIFF_file()
        => Assert.Throws<InvalidDataException>(() => WavAudio.ReadPcm16(new byte[64]));

    [Fact]
    public void ReadPcm16_rejects_a_file_with_no_data_chunk()
    {
        byte[] wav = WavAudio.Write([], channels: 1, sampleRate: 8000);
        byte[] noData = wav[..36]; // everything up to (not including) the "data" chunk

        Assert.Throws<InvalidDataException>(() => WavAudio.ReadPcm16(noData));
    }
}
