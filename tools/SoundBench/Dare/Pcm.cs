using System.IO;
using JackAll.Tools.Audio;
using JackAll.Tools.Spk;
using NVorbis;

namespace SoundBench.Dare;

/// <summary>An audio record decoded: interleaved samples in -1..1.</summary>
public sealed record Pcm(float[] Samples, int Channels, int Rate)
{
    public int Frames => Samples.Length / Channels;

    public static Pcm Decode(SpkBankRecord audio)
    {
        if (SpkBank.DescribeAudio(audio) is { Ogg: true })
        {
            using var reader = new VorbisReader(new MemoryStream(audio.Data), closeOnDispose: true);
            var samples = new List<float>();
            var buffer = new float[4096 * reader.Channels];
            int read;
            while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                samples.AddRange(buffer.AsSpan(0, read));
            }
            return new Pcm([.. samples], reader.Channels, reader.SampleRate);
        }

        ImaAdpcm.DecodedAudio decoded = ImaAdpcm.Decode(audio.Data);
        return new Pcm([.. decoded.Samples.Select(s => s / 32768f)], decoded.Channels, audio.SampleRate ?? SpkBank.FallbackSampleRate);
    }
}
