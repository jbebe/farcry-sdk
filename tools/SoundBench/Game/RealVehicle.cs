namespace SoundBench.Game;

/// <summary>A real vehicle the vehicle overhaul matches the game's cars to: its engine's idle and redline
/// (0 for the overhaul's defaults) and the bank it plays on a gear change.</summary>
/// <remarks>Mirrors <c>mods/vehicle-overhaul/src/real_vehicle.cpp</c>; keep the two in step.</remarks>
public sealed record RealVehicle(string Name, float Mass, float EnginePower, float Idle = 0, float Redline = 0, uint? ShiftSound = null)
{
    private const uint ShiftClunks = 0x00FD0000;

    public static readonly RealVehicle[] All =
    [
        new("Land Rover Series III", 1600, 95),
        new("Datsun 1200", 1000, 90, 750, 6500, ShiftClunks),
        new("Buggy", 800, 87),
        new("Jeep Liberty", 1500, 95),
        new("Jeep Wrangler", 1200, 88),
        new("Jeep Wrangler taxi", 1200, 50),
        new("Quad", 600, 80),
        new("Unimog", 1600, 88),
        new("ZIL-130", 4000, 150, 560, 2700, ShiftClunks),
    ];

    public static RealVehicle? Match(float mass, float enginePower)
        => All.FirstOrDefault(v => Math.Abs(v.Mass - mass) < 1 && Math.Abs(v.EnginePower - enginePower) < 0.5f);
}
