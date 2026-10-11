using SoundBench.Dare;
using SoundBench.Game;

namespace SoundBench.Vehicle;

public enum EngineState
{
    Off,
    Starting,
    Running,
}

/// <summary>
/// The events a car starts and stops as its engine and pedal change, as <c>UpdateSounds</c> does (see
/// audio-runtime.md, "What plays when"), with the vehicle overhaul's gear-change sound, and its tyres.
/// </summary>
/// <param name="play">Starts a slot's event; null for an unset one.</param>
public sealed class VehicleSounds(Car car, Func<SoundSlot, SoundInstance?> play)
{
    /// <summary>The fade of a loop the engine cuts.</summary>
    private const float CutFade = 0.02f;

    private readonly CarSlots _slots = CarSlots.Of(car);
    private readonly List<SoundInstance> _loops = [];
    private readonly List<SoundInstance> _tyres = [];
    private SoundInstance? _pedal;
    private float _startIn;
    private bool _braking;

    public EngineState Engine { get; private set; }

    public void StartEngine()
    {
        if (Engine != EngineState.Off)
        {
            return;
        }
        play(_slots.Ignition);
        Engine = EngineState.Starting;
        _startIn = car.Physics.StartTime;
    }

    public void StopEngine()
    {
        if (Engine == EngineState.Off)
        {
            return;
        }
        Engine = EngineState.Off;
        _loops.ForEach(l => l.Stop(CutFade));
        _loops.Clear();
        _pedal?.Stop(CutFade);
        _pedal = null;
        play(_slots.TurnOff);
        play(_slots.StopIdleLoop);
    }

    /// <summary>Starts what plays continuously again, after the playing events were dropped.</summary>
    public void Restart()
    {
        _loops.Clear();
        _pedal = null;
        if (Engine == EngineState.Running)
        {
            StartLoops();
        }
        RestartTyres();
    }

    /// <summary>Starts the tyres again, which roll as long as the car is on the bench.</summary>
    public void RestartTyres()
    {
        _tyres.ForEach(t => t.Stop(CutFade));
        _tyres.Clear();
        _tyres.AddRange(_slots.Tyres.Select(play).OfType<SoundInstance>());
    }

    /// <param name="pedal">The physics pedal: throttle up to 1, brake down to -1.</param>
    /// <param name="shift">A gear change to be heard now, or 0.</param>
    public void Step(float seconds, float pedal, int shift)
    {
        if (Engine == EngineState.Starting && (_startIn -= seconds) < 0)
        {
            Engine = EngineState.Running;
            StartLoops();
        }
        if (Engine != EngineState.Running)
        {
            return;
        }

        float threshold = car.Sounds.ThrustPedalStopThreshold;
        if (pedal > threshold && _pedal is null)
        {
            _pedal = play(_slots.ThrustPedal);
        }
        else if (pedal <= threshold && _pedal is not null)
        {
            _pedal.Stop(car.Sounds.ThrustPedalStopFadeOut);
            _pedal = null;
        }

        bool braking = pedal < -0.5f;
        if (braking && !_braking)
        {
            play(_slots.Brake);
        }
        _braking = braking;

        if (shift != 0)
        {
            play(_slots.GearChange);
        }
    }

    private void StartLoops()
        => _loops.AddRange(new[] { _slots.IdleLoop, _slots.EngineLoop, _slots.ExtraTorqueLoop }.Select(play).OfType<SoundInstance>());
}
