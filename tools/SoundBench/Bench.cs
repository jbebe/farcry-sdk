using JackAll.Tools.Spk;
using NAudio.Wave;
using SoundBench.Dare;
using SoundBench.Game;
using SoundBench.Vehicle;

namespace SoundBench;

/// <summary>A game parameter the car's multilayers ask for: the range the bench offers and where its value comes from.</summary>
public sealed record GameParameter(string Name, uint Id, float Min, float Max, Func<float> Live);

/// <summary>
/// A car driven and heard. The audio device pulls it a block at a time, and each block first steps the
/// drive, the engine and its sounds, then evaluates every playing tree at the new parameters. Everything
/// else touches it under <see cref="Sync"/>.
/// </summary>
public sealed class Bench : ISampleProvider
{
    /// <summary>The damage parameter is health × 100.</summary>
    private const float Undamaged = 100;

    /// <summary>What the vehicle overhaul answers for the pedal during a gear change, and before the engine catches
    /// (kLiftedPedal and kCrankingPedal in vehicle_sound.cpp).</summary>
    private const float LiftedPedal = -100, CrankingPedal = -200;

    private readonly float[] _block = new float[Mixer.BlockFrames * 2];
    private readonly List<SoundInstance> _started = [];
    private int _blockAt = Mixer.BlockFrames * 2;
    private SoundGraph? _graph;
    private Dictionary<uint, GameParameter> _parameterById = [];

    public object Sync { get; } = new();

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Mixer.Rate, 2);

    public Mixer Mixer { get; } = new();

    /// <summary>Seconds the bench has run.</summary>
    public float Time { get; private set; }

    public Car? Car { get; private set; }
    public HavokStandIn? Havok { get; private set; }
    public Drivetrain? Drivetrain { get; private set; }
    public VehicleSounds? Sounds { get; private set; }
    public IReadOnlyList<GameParameter> Parameters { get; private set; } = [];

    /// <summary>The pedals, each 0..1.</summary>
    public float Throttle { get; set; }
    public float Brake { get; set; }

    public float ShiftTime { get; set; }

    /// <summary>The gearbox keeps its gear, so the revs run from idle to the redline in it.</summary>
    public bool HoldGear { get; set; }

    /// <summary>Parameter values that override what the drive gives.</summary>
    public Dictionary<uint, float> Pins { get; } = [];

    public HashSet<uint> Muted { get; } = [];
    public HashSet<uint> Soloed { get; } = [];

    /// <summary>Metres from the listener to the car.</summary>
    public float Distance { get; set; }

    /// <summary>The material switch value the tyres roll on, or null for each switch's default.</summary>
    public uint? Surface { get; set; }

    public float Volume { get; set; } = 1;

    public void Select(Car car, OverhaulTuning tuning, SoundRegistry registry)
    {
        Car = car;
        ShiftTime = tuning.ShiftTime;
        Drivetrain drivetrain = Drivetrain = new Drivetrain(car.Real?.Idle ?? 0, car.Real?.Redline ?? 0, car.Real?.CatchTime ?? 0);
        HavokStandIn havok = Havok = new HavokStandIn(car.Physics, tuning, car.Real?.Gears, drivetrain.HavokRpm(car.Real?.Downshift ?? 0));
        Sounds = new VehicleSounds(car, Play);

        CarParameters ids = car.Sounds.Parameters;
        Parameters =
        [
            new("Speed (m/s)", ids.Speed, 0, 20, () => havok.Speed),
            new("RPM", ids.Rpm, 0, drivetrain.Redline, () => drivetrain.Rpm),
            new("Pedal", ids.ThrustPedal, CrankingPedal, 100,
                () => drivetrain.Lifted ? LiftedPedal : float.Lerp(CrankingPedal, Throttle * 100, drivetrain.Caught)),
            new("Extra torque", ids.ExtraTorque, 0, 100, () => 0),
            new("Wheel slip", ids.WheelSlip, 0, 15, () => 0),
            new("Damage", ids.Damage, 0, 100, () => Undamaged),
        ];
        _parameterById = Parameters.DistinctBy(p => p.Id).ToDictionary(p => p.Id);
        Pins.Clear();
        Load(registry);
    }

    /// <summary>Swaps in a registry of freshly read banks, restarting what was playing.</summary>
    public void Load(SoundRegistry registry)
    {
        _graph = new SoundGraph(registry, SwitchValue, new RandomPicks());
        Mixer.Playing.Clear();
        _started.Clear();
        Sounds?.Restart();
    }

    /// <summary>The driver gets in and turns the key.</summary>
    public void StartEngine()
    {
        if (Sounds is { Engine: EngineState.Off })
        {
            Drivetrain?.Enter();
            Sounds.StartEngine();
        }
    }

    /// <summary>The events started since the last call, finished or not.</summary>
    public IReadOnlyList<SoundInstance> TakeStarted()
    {
        SoundInstance[] started = [.. _started];
        _started.Clear();
        return started;
    }

    /// <summary>The value a game parameter has for the car now; null for one a car does not answer.</summary>
    public float? Parameter(uint id)
        => Pins.TryGetValue(id, out float pinned) ? pinned
            : _parameterById.TryGetValue(id, out GameParameter? parameter) ? parameter.Live()
            : null;

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
            float throttle = Sounds.Engine == EngineState.Running ? Throttle : 0;
            Havok.Step(Mixer.BlockSeconds, throttle, Brake, ShiftTime, HoldGear);
            int shift = Drivetrain.Step(Havok.Rpm, Havok.Gear, throttle, ShiftTime, Mixer.BlockSeconds);
            Sounds.Step(Mixer.BlockSeconds, throttle > 0 ? throttle : -Brake, shift);

            var listening = new Listening(Parameter, Muted, Soloed, Distance);
            foreach (SoundInstance instance in Mixer.Playing)
            {
                instance.Root.Evaluate(listening);
            }
        }
        Mixer.Render(_block, Volume);
        Time += Mixer.BlockSeconds;
    }

    private SoundInstance? Play(SoundSlot slot)
    {
        if (slot.Id == SpkLayout.NoId || _graph is null)
        {
            return null;
        }
        var instance = new SoundInstance(slot.Field, _graph.Event(slot.Id), Time);
        Mixer.Playing.Add(instance);
        _started.Add(instance);
        return instance;
    }

    private uint? SwitchValue(uint group)
        => group == Car?.Sounds.ReliabilitySwitch ? Car.Sounds.NoDamage
            : group == Car?.Sounds.MaterialSwitch ? Surface
            : null;
}
