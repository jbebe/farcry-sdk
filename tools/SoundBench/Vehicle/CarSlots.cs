using JackAll.Tools.Spk;
using SoundBench.Game;

namespace SoundBench.Vehicle;

/// <summary>An event a car plays, under the field that names it.</summary>
public sealed record SoundSlot(string Field, uint Id);

/// <summary>The events a car plays, in the order the bench lists them.</summary>
public sealed record CarSlots(
    SoundSlot Ignition,
    SoundSlot IdleLoop,
    SoundSlot EngineLoop,
    SoundSlot ExtraTorqueLoop,
    SoundSlot ThrustPedal,
    SoundSlot Brake,
    SoundSlot GearChange,
    SoundSlot TurnOff,
    SoundSlot StopIdleLoop,
    IReadOnlyList<SoundSlot> Tyres)
{
    public static CarSlots Of(Car car)
    {
        CarSounds s = car.Sounds;
        return new CarSlots(
            new("sndEngineIgnition", s.Ignition),
            new("sndPlayEngineIdleLoop", s.IdleLoop),
            new("sndEngineLoop", s.EngineLoop),
            new("sndExtraTorqueEngineLoop", s.ExtraTorqueLoop),
            new("sndThrustPedal", s.ThrustPedal),
            new("sndBrake", s.Brake),
            car.Real?.ShiftSound is { } overhaul ? new("gear change (Vehicle Overhaul)", overhaul) : new("sndGearShift_New", s.GearShift),
            new("sndTurnOffEngine", s.TurnOff),
            new("sndStopEngineIdleLoop", s.StopIdleLoop),
            [.. s.WheelRolls.Select((id, i) => new SoundSlot($"sndWheelRoll_{i + 1}", id))]);
    }

    /// <summary>Every slot the car sets, in order.</summary>
    public IReadOnlyList<SoundSlot> All =>
    [
        .. new[] { Ignition, IdleLoop, EngineLoop, ExtraTorqueLoop, ThrustPedal, Brake, GearChange, TurnOff, StopIdleLoop }
            .Concat(Tyres)
            .Where(s => s.Id != SpkLayout.NoId),
    ];
}
