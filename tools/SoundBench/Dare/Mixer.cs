namespace SoundBench.Dare;

/// <summary>Mixes the playing events into 48 kHz stereo, one block at a time.</summary>
public sealed class Mixer
{
    public const int Rate = 48000;
    public const int BlockFrames = 480;
    public const float BlockSeconds = BlockFrames / (float)Rate;

    public List<SoundInstance> Playing { get; } = [];

    /// <summary>The loudest output sample since <see cref="TakeOutputPeak"/>, after the volume, before the clip.</summary>
    private float _outputPeak;

    /// <summary>The output's peak in dBFS since the last call, above 0 when the mix clipped.</summary>
    public float TakeOutputPeak()
    {
        float peak = _outputPeak;
        _outputPeak = 0;
        return peak > 0 ? 20 * MathF.Log10(peak) : -96;
    }

    /// <summary>Fills a block of <see cref="BlockFrames"/> interleaved stereo frames.</summary>
    public void Render(Span<float> block, float volume)
    {
        block.Clear();
        foreach (SoundInstance instance in Playing)
        {
            (float from, float to) = instance.Advance(BlockSeconds);
            foreach (Voice voice in instance.Voices)
            {
                Render(voice, from, to, block);
            }
        }
        Playing.RemoveAll(i => i.Gone);

        for (int i = 0; i < block.Length; i++)
        {
            float sample = block[i] * volume;
            _outputPeak = Math.Max(_outputPeak, Math.Abs(sample));
            block[i] = Math.Clamp(sample, -1, 1);
        }
    }

    /// <summary>Adds one voice, resampled by its pitch with linear interpolation, its gain ramped across the block.</summary>
    private static void Render(Voice voice, float fadeFrom, float fadeTo, Span<float> block)
    {
        voice.PeakDb = -96;
        if (voice.Done || voice.Frames < 2)
        {
            voice.Done = true;
            return;
        }

        float[] samples = voice.Pcm.Samples;
        int channels = voice.Pcm.Channels;
        double step = voice.Pitch * voice.Pcm.Rate / Rate;
        float from = voice.Gain * fadeFrom, to = voice.TargetGain * fadeTo;
        int frames = block.Length / 2;
        float peak = 0;
        for (int i = 0; i < frames; i++)
        {
            int at = (int)voice.Position;
            int next = at + 1 < voice.Frames ? at + 1 : voice.Loop ? 0 : at;
            float t = (float)(voice.Position - at);
            float gain = from + (to - from) * i / frames;
            float left = samples[at * channels] + (samples[next * channels] - samples[at * channels]) * t;
            float right = channels == 1 ? left : samples[at * 2 + 1] + (samples[next * 2 + 1] - samples[at * 2 + 1]) * t;
            block[i * 2] += left * gain;
            block[i * 2 + 1] += right * gain;
            peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)) * gain);

            voice.Position += step;
            if (voice.Position >= voice.Frames)
            {
                if (!voice.Loop)
                {
                    voice.Done = true;
                    break;
                }
                voice.Position %= voice.Frames;
            }
        }
        voice.Gain = voice.TargetGain;
        voice.PeakDb = peak > 0 ? Math.Max(-96, 20 * MathF.Log10(peak)) : -96;
    }
}
