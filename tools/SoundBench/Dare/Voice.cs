using JackAll.Tools.Spk;

namespace SoundBench.Dare;

/// <summary>One sample playing: its audio, where it has got to, and the gain it ramps to over a block.</summary>
/// <param name="frames">The frames the game plays, from the sample's declared length rather than the stream's.</param>
/// <param name="rolloff">The rolloff of the event playing it; null with <paramref name="positioned"/> set is a
/// positioned voice with no curve.</param>
public sealed class Voice(Pcm pcm, bool loop, int frames, bool positioned, IReadOnlyList<SpkPoint>? rolloff)
{
    /// <summary>Where DARE drops a positioned voice even with voices free.</summary>
    public const float AudibleFloorDb = -48;

    public Pcm Pcm { get; } = pcm;
    public bool Loop { get; } = loop;
    public int Frames { get; } = frames;

    public double Position { get; set; }
    public bool Done { get; set; }
    public float Pitch { get; private set; } = 1;
    public float Gain { get; set; }
    public float TargetGain { get; private set; }

    /// <summary>Its volume after DARE's clamp to -96..0 dB and the rolloff at the listener's distance.</summary>
    public float HeardDb { get; private set; } = -96;
    public bool Virtual { get; private set; }

    /// <summary>The loudest sample it put out in the last block.</summary>
    public float PeakDb { get; set; } = -96;

    public void Update(float volumeDb, float pitch, bool audible, float distance)
    {
        float rolloffDb = !positioned ? 0 : rolloff is null ? -96 : SpkCurve.Evaluate(rolloff, distance);
        HeardDb = Math.Max(-96, Math.Clamp(volumeDb, -96, 0) + rolloffDb);
        Virtual = positioned && HeardDb < AudibleFloorDb;
        Pitch = Math.Max(pitch, 0.01f);
        TargetGain = audible && !Virtual && HeardDb > -96 ? MathF.Pow(10, HeardDb / 20) : 0;
    }
}
