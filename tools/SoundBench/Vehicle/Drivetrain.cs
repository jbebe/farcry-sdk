namespace SoundBench.Vehicle;

/// <summary>
/// The vehicle overhaul's engine: <c>Drivetrain::Step</c> in mods/vehicle-overhaul/src/drivetrain.cpp, fed
/// by the stand-in instead of Havok. Keep the two in step.
/// </summary>
public sealed class Drivetrain(float idle, float redline)
{
    private const float HavokMin = HavokStandIn.MinRpm, HavokUpshift = HavokStandIn.UpshiftRpm;
    private const float ClutchSlip = 1750f / 5750f, RiseRate = 7000f / 5750f, FallRate = 3000f / 5750f;

    private int _gear;
    private float _shiftTime;
    private float _sinceShift;
    private int _pendingShift;

    public float Idle { get; } = idle > 0 ? idle : 850;
    public float Redline { get; } = redline > 0 ? redline : HavokUpshift;
    public float Rpm { get; private set; } = idle > 0 ? idle : 850;

    /// <summary>The driver is off the throttle for a gear change.</summary>
    public bool Lifted { get; private set; }

    /// <summary>Moves the revs on; returns a gear change due to be heard now, +1 up or -1 down, else 0.</summary>
    public int Step(float havokRpm, int gear, float throttle, float shiftTime, float seconds)
    {
        int heard = 0;
        _sinceShift += seconds;
        if (gear != _gear)
        {
            _pendingShift = gear > _gear ? 1 : -1;
            _shiftTime = shiftTime;
            _sinceShift = 0;
            _gear = gear;
        }
        if (_pendingShift != 0 && _sinceShift >= 0.5f * _shiftTime)
        {
            heard = _pendingShift;
            _pendingShift = 0;
        }
        Lifted = _sinceShift < _shiftTime;

        float range = Redline - Idle;
        float wheels = Idle + (Math.Abs(havokRpm) - HavokMin) * range / (HavokUpshift - HavokMin);
        float target = Math.Max(Idle, wheels);
        float slip = Idle + ClutchSlip * range;
        if (wheels < slip)
        {
            target = Math.Max(target, Idle + throttle * (slip - Idle));
        }
        target = Math.Min(target, Redline);

        Rpm += Math.Clamp(target - Rpm, -FallRate * range * seconds, RiseRate * range * seconds);
        return heard;
    }
}
