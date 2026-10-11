using JackAll.App;

namespace SoundBench.Views;

/// <summary>A game parameter the car answers: its live value, or a pinned one that overrides the drive.</summary>
public sealed class ParameterView(Bench bench, GameParameter parameter) : Observable
{
    private float _pinValue = parameter.Min;
    private bool _pinned;
    private float? _live;
    private string _value = "";

    public string Name => parameter.Name;
    public float Min => parameter.Min;
    public float Max => parameter.Max;
    public string Value { get => _value; private set => Set(ref _value, value); }

    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (Set(ref _pinned, value))
            {
                Write();
            }
        }
    }

    public float PinValue
    {
        get => _pinValue;
        set
        {
            if (Set(ref _pinValue, value) && _pinned)
            {
                Write();
            }
        }
    }

    /// <summary>Reads the value; call under <see cref="Bench.Sync"/>.</summary>
    public void Capture() => _live = bench.Parameter(parameter.Id);

    /// <summary>Shows what <see cref="Capture"/> read; an unpinned slider follows it.</summary>
    public void Publish()
    {
        Value = _live is { } v ? $"{v:0.##}" : "-";
        if (!_pinned && _live is { } live)
        {
            Set(ref _pinValue, Math.Clamp(live, Min, Max), nameof(PinValue));
        }
    }

    private void Write()
    {
        lock (bench.Sync)
        {
            if (_pinned)
            {
                bench.Pins[parameter.Id] = _pinValue;
            }
            else
            {
                bench.Pins.Remove(parameter.Id);
            }
        }
    }
}
