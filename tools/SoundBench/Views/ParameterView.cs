namespace SoundBench.Views;

/// <summary>A game parameter the car answers: its live value, or a pinned one that overrides the drive.</summary>
/// <param name="Max">The range <c>7fffffff.bao</c> declares for it, which the slider covers.</param>
public sealed class ParameterView(Bench bench, string name, uint id, float min, float max) : Observable
{
    private float _pinValue = min;

    public string Name { get; } = name;
    public uint Id { get; } = id;
    public float Min { get; } = min;
    public float Max { get; } = max;
    public string Value { get; private set; } = "";

    public bool Pinned
    {
        get
        {
            lock (bench.Sync)
            {
                return bench.Pins.ContainsKey(Id);
            }
        }
        set
        {
            lock (bench.Sync)
            {
                if (value)
                {
                    bench.Pins[Id] = _pinValue;
                }
                else
                {
                    bench.Pins.Remove(Id);
                }
            }
            Notify();
        }
    }

    public float PinValue
    {
        get => _pinValue;
        set
        {
            _pinValue = value;
            lock (bench.Sync)
            {
                if (bench.Pins.ContainsKey(Id))
                {
                    bench.Pins[Id] = value;
                }
            }
            Notify();
        }
    }

    /// <summary>Reads the value; call under <see cref="Bench.Sync"/>.</summary>
    public void Refresh()
    {
        float? value = bench.Parameter(Id);
        Value = value is { } v ? $"{v:0.##}" : "-";
        if (!bench.Pins.ContainsKey(Id) && value is { } live)
        {
            _pinValue = Math.Clamp(live, Min, Max);
        }
    }

    public void Publish()
    {
        Notify(nameof(Value));
        Notify(nameof(PinValue));
    }
}
