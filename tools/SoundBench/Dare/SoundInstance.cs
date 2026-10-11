namespace SoundBench.Dare;

/// <summary>One event playing, under the field that played it, and the fade that stops it.</summary>
public sealed class SoundInstance(string field, SoundNode root)
{
    /// <summary>How long a finished event stays listed.</summary>
    private const float Linger = 1.5f;

    private float _fade = 1;
    private float _fadeRate;
    private float _finishedFor;

    public string Field { get; } = field;
    public SoundNode Root { get; } = root;
    public Voice[] Voices { get; } = [.. root.Subtree().Select(n => n.Voice).OfType<Voice>()];

    /// <summary>Plays for as long as the car is on the bench, under the engine's events.</summary>
    public bool Background { get; set; }

    public bool Gone => _finishedFor > Linger;

    public void Stop(float fadeSeconds) => _fadeRate = fadeSeconds > 0 ? 1 / fadeSeconds : float.PositiveInfinity;

    /// <summary>Moves the fade on by a block; returns the fade at its start and end.</summary>
    public (float From, float To) Advance(float seconds)
    {
        float from = _fade;
        _fade = Math.Max(0, _fade - _fadeRate * seconds);
        if (_fade <= 0 || Voices.All(v => v.Done))
        {
            _finishedFor += seconds;
        }
        return (from, _fade);
    }
}
