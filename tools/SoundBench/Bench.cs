using JackAll.Tools.Spk;
using NAudio.Wave;
using SoundBench.Dare;
using SoundBench.Game;
using SoundBench.Vehicle;

namespace SoundBench;

/// <summary>
/// A car driven and heard. The audio device pulls it a block at a time, and each block first steps the
/// drive, the engine and its sounds, then evaluates every playing tree at the new parameters. Everything
/// else touches it under <see cref="Sync"/>.
/// </summary>
public sealed class Bench : ISampleProvider
{
    private readonly float[] _block = new float[Mixer.BlockFrames * 2];
    private readonly List<SoundInstance> _wheels = [];
    private int _blockAt = Mixer.BlockFrames * 2;
    private RandomPicks _picks = new();
    private SoundRegistry? _registry;

    public object Sync { get; } = new();

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Mixer.Rate, 2);

    public Mixer Mixer { get; } = new();

    public Car? Car { get; private set; }
    public OverhaulTuning Tuning { get; private set; } = new();
    public HavokStandIn? Havok { get; private set; }
    public Drivetrain? Drivetrain { get; private set; }
    public VehicleSounds? Sounds { get; private set; }

    /// <summary>The pedals, each 0..1.</summary>
    public float Throttle { get; set; }
    public float Brake { get; set; }

    public float ShiftTime { get; set; }

    /// <summary>Parameter values that override what the drive gives.</summary>
    public Dictionary<uint, float> Pins { get; } = [];

    public HashSet<uint> Muted { get; } = [];
    public HashSet<uint> Soloed { get; } = [];

    /// <summary>Metres from the listener to the car.</summary>
    public float Distance { get; set; }

    /// <summary>The material switch value the tyres roll on, or null for each switch's default.</summary>
    public uint? Surface { get; set; }

    public float Volume { get; set; } = 1;

    /// <summary>A health of 0..100 for the damage parameter; 100 is new.</summary>
    public const float Undamaged = 100;

    public void Select(Car car, OverhaulTuning tuning, SoundRegistry registry)
    {
        Car = car;
        Tuning = tuning;
        ShiftTime = tuning.ShiftTime;
        Havok = new HavokStandIn(car.Physics, tuning);
        Drivetrain = new Drivetrain(car.Real?.Idle ?? 0, car.Real?.Redline ?? 0);
        Sounds = new VehicleSounds(car, Play);
        Load(registry);
    }

    /// <summary>Swaps in a registry of freshly read banks, restarting what was playing.</summary>
    public void Load(SoundRegistry registry)
    {
        _registry = registry;
        _picks = new RandomPicks();
        Mixer.Playing.Clear();
        _wheels.Clear();
        if (Car is not null)
        {
            foreach (uint id in VehicleSounds.Events(Car))
            {
                registry.Hold(id);
            }
        }
        RestartWheels();
        Sounds?.Restart();
    }

    /// <summary>The value a game parameter has for the car now; null for one a car does not answer.</summary>
    public float? Parameter(uint id)
    {
        if (Pins.TryGetValue(id, out float pinned))
        {
            return pinned;
        }
        if (Car?.Sounds.Parameters is not { } p || Havok is null || Drivetrain is null)
        {
            return null;
        }
        return id == p.Speed ? Havok.Speed
            : id == p.Rpm ? Drivetrain.Rpm
            : id == p.ThrustPedal ? (Drivetrain.Lifted ? -100 : Throttle * 100)
            : id == p.ExtraTorque || id == p.WheelSlip ? 0
            : id == p.Damage ? Undamaged
            : null;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (Sync)
        {
            for (int i = 0; i < count; i++)
            {
                if (_blockAt == _block.Length)
                {
                    Step();
                    _blockAt = 0;
                }
                buffer[offset + i] = _block[_blockAt++];
            }
        }
        return count;
    }

    private void Step()
    {
        if (Havok is not null && Drivetrain is not null && Sounds is not null)
        {
            float throttle = Sounds.Running ? Throttle : 0;
            Havok.Step(Mixer.BlockSeconds, throttle, Brake, ShiftTime);
            int shift = Drivetrain.Step(Havok.Rpm, Havok.Gear, throttle, ShiftTime, Mixer.BlockSeconds);
            Sounds.Step(Mixer.BlockSeconds, throttle > 0 ? throttle : -Brake, shift);

            var listening = new Listening(Parameter, Muted, Soloed, Distance);
            foreach (SoundInstance instance in Mixer.Playing)
            {
                instance.Root.Evaluate(listening);
            }
        }
        Mixer.Render(_block, Volume);
    }

    private SoundInstance? Play(uint id, string field)
    {
        if (id == SpkLayout.NoId || _registry is null || Car is null)
        {
            return null;
        }
        var graph = new SoundGraph(_registry, SwitchValue, _picks);
        var instance = new SoundInstance(field, graph.Event(id));
        Mixer.Playing.Add(instance);
        return instance;
    }

    private uint? SwitchValue(uint group)
        => group == Car?.Sounds.ReliabilitySwitch ? Car.Sounds.NoDamage
            : group == Car?.Sounds.MaterialSwitch ? Surface
            : null;

    /// <summary>Starts the tyres again, as a switch picks its case only when its event starts. They roll for as
    /// long as the car is on the bench; their multilayers fade them with speed.</summary>
    public void RestartWheels()
    {
        _wheels.ForEach(w => w.Stop(0.05f));
        _wheels.Clear();
        foreach ((uint id, int wheel) in (Car?.Sounds.WheelRolls ?? []).Select((id, i) => (id, i)))
        {
            if (Play(id, $"sndWheelRoll_{wheel + 1}") is { } instance)
            {
                _wheels.Add(instance);
            }
        }
    }
}
