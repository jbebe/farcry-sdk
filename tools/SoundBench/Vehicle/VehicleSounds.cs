using JackAll.Tools.Spk;
using SoundBench.Dare;
using SoundBench.Game;

namespace SoundBench.Vehicle;

/// <summary>
/// What <c>CVehicleTypeWheeled::UpdateSounds</c> starts and stops as the engine and pedal change
/// (docs/docs/engine-internals/audio-runtime.md, "What plays when"), with the vehicle overhaul's gear-change sound.
/// </summary>
/// <param name="play">Starts an event under the field that names it; null for an unset one.</param>
public sealed class VehicleSounds(Car car, Func<uint, string, SoundInstance?> play)
{
    /// <summary>How quickly a loop the engine stops fades, so it does not click.</summary>
    private const float CutFade = 0.02f;

    private readonly List<SoundInstance> _loops = [];
    private SoundInstance? _pedal;
    private float _startIn = -1;
    private bool _braking;

    public bool Running { get; private set; }

    public bool Starting => _startIn >= 0;

    public void StartEngine()
    {
        if (Running || Starting)
        {
            return;
        }
        play(car.Sounds.Ignition, "sndEngineIgnition");
        _startIn = car.Physics.StartTime;
    }

    public void StopEngine()
    {
        if (!Running && !Starting)
        {
            return;
        }
        _startIn = -1;
        Running = false;
        _loops.ForEach(l => l.Stop(CutFade));
        _loops.Clear();
        _pedal?.Stop(CutFade);
        _pedal = null;
        play(car.Sounds.TurnOff, "sndTurnOffEngine");
        play(car.Sounds.StopIdleLoop, "sndStopEngineIdleLoop");
    }

    /// <summary>Starts the engine's loops again, for banks reloaded while it runs.</summary>
    public void Restart()
    {
        _loops.Clear();
        _pedal = null;
        if (Running)
        {
            StartLoops();
        }
    }

    /// <param name="pedal">The physics pedal: throttle up to 1, brake down to -1.</param>
    /// <param name="shift">A gear change to be heard now, or 0.</param>
    public void Step(float seconds, float pedal, int shift)
    {
        if (Starting && (_startIn -= seconds) < 0)
        {
            _startIn = -1;
            Running = true;
            StartLoops();
        }
        if (!Running)
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
