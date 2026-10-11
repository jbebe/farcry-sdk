namespace SoundBench.Game;

/// <summary>A wheeled vehicle as its archetype sets it up, plus the real vehicle the overhaul knows it as.</summary>
/// <param name="Archetypes">Every archetype sharing this sound and physics setup.</param>
public sealed record Car(IReadOnlyList<string> Archetypes, RealVehicle? Real, CarSounds Sounds, CarPhysics Physics)
{
    public string Label => (Real?.Name ?? Archetypes[0].Replace("vehicle.", "", StringComparison.OrdinalIgnoreCase))
                           + (Archetypes.Count > 1 ? $" ({Archetypes.Count} archetypes)" : "");

    public override string ToString() => Label;
}

/// <summary>The events a car's <c>Sound</c> block names, the game parameters its <c>SoundSettings</c> answer,
/// and the switches it sets. An unset event is <see cref="JackAll.Tools.Spk.SpkLayout.NoId"/>.</summary>
public sealed record CarSounds(
    uint Ignition,
    uint IdleLoop,
    uint StopIdleLoop,
    uint EngineLoop,
    uint ExtraTorqueLoop,
    uint TurnOff,
    uint ThrustPedal,
    uint Brake,
    uint GearShift,
    IReadOnlyList<uint> WheelRolls,
    float ThrustPedalStopThreshold,
    float ThrustPedalStopFadeOut,
    CarParameters Parameters,
    uint ReliabilitySwitch,
    uint NoDamage,
    uint MaterialSwitch);

/// <summary>The ids a car's multilayers ask it for.</summary>
public sealed record CarParameters(uint Speed, uint Rpm, uint ThrustPedal, uint ExtraTorque, uint WheelSlip, uint Damage);

/// <summary>What the archetype's <c>WheeledParams</c> give Havok, and how long the engine takes to start.</summary>
/// <param name="TopSpeedMph">fGearBoxTopSpeed, which Havok reads in mph.</param>
/// <param name="BrakingTorque">Summed over the wheels.</param>
public sealed record CarPhysics(float Mass, float EnginePower, float TopSpeedMph, int Gears, float BrakingTorque, float StartTime);
