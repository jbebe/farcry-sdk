namespace SoundBench.Dare;

/// <summary>One event playing, under the field that played it, and the fade that stops it.</summary>
public sealed class SoundInstance(string field, SoundNode root, float startedAt)
{
    private float _fade = 1;
    private float _fadeRate;

    public string Field { get; } = field;
    public SoundNode Root { get; } = root;
    public Voice[] Voices { get; } = [.. root.Subtree().Select(n => n.Voice).OfType<Voice>()];

    /// <summary>The bench's time when it started, in seconds.</summary>
    public float StartedAt { get; } = startedAt;

    public bool Stopping => _fadeRate > 0;
    public bool Finished => _fade <= 0 || Voices.All(v => v.Done);

    public void Stop(float fadeSeconds) => _fadeRate = fadeSeconds > 0 ? 1 / fadeSeconds : float.PositiveInfinity;

    /// <summary>Moves the fade on by a block; returns the fade at its start and end.</summary>
    public (float From, float To) Advance(float seconds)
    {
        float from = _fade;
        _fade = Math.Max(0, _fade - _fadeRate * seconds);
        return (from, _fade);
    }
}
