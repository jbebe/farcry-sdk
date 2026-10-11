using System.IO;
using JackAll.Tools.Audio;
using JackAll.Tools.Spk;
using NVorbis;

namespace SoundBench.Dare;

/// <summary>An audio record decoded: interleaved samples in -1..1.</summary>
public sealed record Pcm(float[] Samples, int Channels, int Rate)
{
    public int Frames => Samples.Length / Channels;

    /// <summary>Null for a record that is neither Ogg Vorbis nor IMA-ADPCM.</summary>
    public static Pcm? Decode(SpkBankRecord audio)
    {
        switch (SpkBank.DescribeAudio(audio))
        {
            case { Ogg: true }:
                using (var reader = new VorbisReader(new MemoryStream(audio.Data), closeOnDispose: true))
                {
                    var samples = new float[reader.TotalSamples * reader.Channels];
                    int read = 0, got;
                    while (read < samples.Length && (got = reader.ReadSamples(samples, read, samples.Length - read)) > 0)
                    {
                        read += got;
                    }
                    return new Pcm(samples[..read], reader.Channels, reader.SampleRate);
                }
            case { Ogg: false }:
                ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(audio.Data);
                var pcm = new float[decoded.Samples.Length];
                for (int i = 0; i < pcm.Length; i++)
                {
                    pcm[i] = decoded.Samples[i] / 32768f;
                }
                return new Pcm(pcm, decoded.Channels, audio.SampleRate ?? SpkBank.FallbackSampleRate);
            default:
                return null;
        }
    }
}
