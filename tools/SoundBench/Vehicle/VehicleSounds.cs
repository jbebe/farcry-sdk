using JackAll.Tools.Spk;
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
/// <param name="play">Starts an event under the field that names it; null for an unset one.</param>
public sealed class VehicleSounds(Car car, Func<uint, string, SoundInstance?> play)
{
    /// <summary>The fade of a loop the engine cuts.</summary>
    private const float CutFade = 0.02f;

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
        play(car.Sounds.Ignition, "sndEngineIgnition");
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
        play(car.Sounds.TurnOff, "sndTurnOffEngine");
        play(car.Sounds.StopIdleLoop, "sndStopEngineIdleLoop");
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
        for (int wheel = 0; wheel < car.Sounds.WheelRolls.Count; wheel++)
        {
            if (play(car.Sounds.WheelRolls[wheel], $"sndWheelRoll_{wheel + 1}") is { } tyre)
            {
                tyre.Background = true;
                _tyres.Add(tyre);
            }
        }
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
            _pedal = play(car.Sounds.ThrustPedal, "sndThrustPedal");
        }
        else if (pedal <= threshold && _pedal is not null)
        {
            _pedal.Stop(car.Sounds.ThrustPedalStopFadeOut);
            _pedal = null;
        }

        bool braking = pedal < -0.5f;
        if (braking && !_braking)
        {
            play(car.Sounds.Brake, "sndBrake");
        }
        _braking = braking;

        if (shift != 0)
        {
            if (car.Real?.ShiftSound is { } overhaul)
            {
                play(overhaul, "gear change (Vehicle Overhaul)");
            }
            else
            {
                play(car.Sounds.GearShift, "sndGearShift_New");
            }
        }
    }

    private void StartLoops()
    {
        SoundInstance?[] loops =
        [
            play(car.Sounds.IdleLoop, "sndPlayEngineIdleLoop"),
            play(car.Sounds.EngineLoop, "sndEngineLoop"),
            play(car.Sounds.ExtraTorqueLoop, "sndExtraTorqueEngineLoop"),
        ];
        _loops.AddRange(loops.OfType<SoundInstance>());
    }

    /// <summary>Every event the car can play, for loading their banks up front.</summary>
    public static IEnumerable<uint> Events(Car car)
    {
        CarSounds s = car.Sounds;
        uint[] ids = [s.Ignition, s.IdleLoop, s.StopIdleLoop, s.EngineLoop, s.ExtraTorqueLoop, s.TurnOff,
            s.ThrustPedal, s.Brake, s.GearShift, .. s.WheelRolls, car.Real?.ShiftSound ?? SpkLayout.NoId];
        return ids.Where(id => id != SpkLayout.NoId);
    }
}
