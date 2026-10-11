namespace SoundBench.Dare;

/// <summary>One event playing, under the field that played it, and the fade that stops it.</summary>
public sealed class SoundInstance(string field, SoundNode root)
{
    /// <summary>How long a finished event stays listed, so a one-shot or a failed play can be seen.</summary>
    private const float Linger = 1.5f;

    private float _fadeRate;
    private float _finishedFor;

    public string Field { get; } = field;
    public SoundNode Root { get; } = root;
    public Voice[] Voices { get; } = [.. root.Subtree().Select(n => n.Voice).OfType<Voice>()];
    public float Fade { get; private set; } = 1;
    public bool Stopping => _fadeRate > 0;
    public bool Finished => Fade <= 0 || Voices.All(v => v.Done);

    public void Stop(float fadeSeconds) => _fadeRate = fadeSeconds > 0 ? 1 / fadeSeconds : float.PositiveInfinity;

    /// <summary>Moves the fade on by a block; returns the fade at its start and end.</summary>
    public (float From, float To) Advance(float seconds)
    {
        float from = Fade;
        Fade = Math.Max(0, Fade - _fadeRate * seconds);
        if (Finished)
        {
            _finishedFor += seconds;
        }
        return (from, Fade);
    }

    public bool Gone => _finishedFor > Linger;
}
