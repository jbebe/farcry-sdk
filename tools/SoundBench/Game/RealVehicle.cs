namespace SoundBench.Game;

/// <summary>A real vehicle the vehicle overhaul matches the game's cars to: its engine's idle and redline
/// (0 for the overhaul's defaults), the bank it plays on a gear change, the seconds from the ignition to its
/// engine catching (0 for none), where its tunings start, by their keys in the tuning file, for those
/// that differ from everyone's defaults, and its gearbox's forward ratios and the revs it changes down at
/// (null for Havok's).</summary>
/// <remarks>Mirrors <c>mods/vehicle-overhaul/src/real_vehicle.cpp</c>.</remarks>
public sealed record RealVehicle(
    string Name, float Mass, float EnginePower, float Idle = 0, float Redline = 0, uint? ShiftSound = null, float CatchTime = 0,
    IReadOnlyDictionary<string, float>? Tuning = null, IReadOnlyList<float>? Gears = null, float Downshift = 0)
{
    private const uint ShiftClunks = 0x00FD0000;

    public static readonly RealVehicle[] All =
    [
        new("Land Rover Series III", 1600, 95, 525, 4000, null, 0.8f,
            new Dictionary<string, float> { ["Top speed"] = 1.2f, ["Springs"] = 1.0f }),
        new("Datsun 1200", 1000, 90, 750, 6500, ShiftClunks, 0.816f, new Dictionary<string, float>
        {
            ["Engine power"] = 0.8f, ["Shift time"] = 0.5f, ["Downshift time"] = 0.5f, ["Steering at speed"] = 1.0f,
            ["Tyre grip"] = 0.4f, ["Rolling resistance"] = 0.0f, ["Inertia"] = 1.02f, ["Springs"] = 0.55f,
            ["Compression damping"] = 0.48f, ["Rebound damping"] = 0.4f,
        }),
        new("Buggy", 800, 87),
        new("Jeep Liberty", 1500, 95),
        new("Jeep Wrangler", 1200, 88),
        new("Jeep Wrangler taxi", 1200, 50),
        new("Quad", 600, 80, 1580, 9300, null, 0.5f),
        new("Unimog", 1600, 88, 600, 2800, ShiftClunks, 0.4f, new Dictionary<string, float>
        {
            ["Engine power"] = 0.5f, ["Top speed"] = 1.65f, ["Shift time"] = 0.8f, ["Downshift time"] = 0.8f,
            ["Brake torque"] = 0.45f, ["Steering lock"] = 0.95f, ["Steering at speed"] = 1.0f, ["Tyre grip"] = 0.6f,
            ["Rolling resistance"] = 0.2f, ["Inertia"] = 2.0f, ["Springs"] = 0.65f, ["Travel"] = 2.0f,
            ["Compression damping"] = 0.5f, ["Rebound damping"] = 0.5f,
        }),
        new("ZIL-130", 4000, 150, 560, 3200, ShiftClunks, 0.7f, new Dictionary<string, float>
        {
            ["Engine power"] = 1.0f, ["Top speed"] = 1.55f, ["Shift time"] = 1.0f, ["Downshift time"] = 1.0f,
            ["Brake torque"] = 0.75f, ["Steering lock"] = 0.85f, ["Steering at speed"] = 1.0f, ["Tyre grip"] = 0.6f,
            ["Rolling resistance"] = 0.2f, ["Inertia"] = 3.6f, ["Springs"] = 0.8f, ["Travel"] = 1.5f,
            ["Compression damping"] = 0.65f, ["Rebound damping"] = 0.65f,
        },
            [7.45f, 4.10f, 2.29f, 1.47f, 1.00f], 1300),
    ];

    public static RealVehicle? Match(float mass, float enginePower)
        => All.FirstOrDefault(v => Math.Abs(v.Mass - mass) < 1 && Math.Abs(v.EnginePower - enginePower) < 0.5f);
}
