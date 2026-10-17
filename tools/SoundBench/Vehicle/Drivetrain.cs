namespace SoundBench.Vehicle;

/// <summary>The vehicle overhaul's engine, fed by the stand-in instead of Havok.</summary>
/// <remarks>Mirrors <c>Drivetrain::Step</c> in <c>mods/vehicle-overhaul/src/drivetrain.cpp</c>.</remarks>
public sealed class Drivetrain
{
    private const float ClutchSlip = 1750f / 5750f, RiseRate = 7000f / 5750f, FallRate = 3000f / 5750f;

    /// <summary>The seconds the engine sound takes to fade in once the engine catches.</summary>
    private const float CatchFade = 0.5f;

    /// <summary>How far, as a share of the range, and how slowly, as two waves' periods in seconds, the revs a
    /// slipping clutch holds wander: a driver's foot is no machine.</summary>
    private const float SlipWander = 0.03f;
    private static readonly double[] WanderPeriods = [2.2, 1.25];

    private readonly float _catchTime;
    private float _sinceEntry;
    private double _clock;
    private int _gear;
    private float _shiftTime;
    private float _sinceShift;
    private int _pendingShift;

    /// <param name="idle">0 for the overhaul's default.</param>
    /// <param name="redline">0 for Havok's upshift revs.</param>
    /// <param name="catchTime">Seconds from the driver getting in to the engine catching, or 0.</param>
    public Drivetrain(float idle, float redline, float catchTime)
    {
        Idle = idle > 0 ? idle : 850;
        Redline = redline > 0 ? redline : HavokStandIn.UpshiftRpm;
        Rpm = Idle;
        _catchTime = catchTime;
    }

    public float Idle { get; }
    public float Redline { get; }
    public float Rpm { get; private set; }

    /// <summary>The driver is off the throttle for a gear change.</summary>
    public bool Lifted { get; private set; }

    /// <summary>How far the engine sound has faded in since the engine caught, 0 to 1.</summary>
    public float Caught { get; private set; } = 1;

    /// <summary>The driver gets in, and the engine catches the catch time from now.</summary>
    public void Enter() => _sinceEntry = 0;

    /// <summary>Havok's revs while this engine turns at <paramref name="rpm"/>.</summary>
    public float HavokRpm(float rpm) =>
        HavokStandIn.MinRpm + (rpm - Idle) * (HavokStandIn.UpshiftRpm - HavokStandIn.MinRpm) / (Redline - Idle);

    /// <summary>Moves the revs on; returns a gear change due to be heard now, +1 up or -1 down, else 0.</summary>
    public int Step(float havokRpm, int gear, float clutchLeft, float speed, float throttle, float seconds)
    {
        int heard = 0;
        _sinceEntry += seconds;
        Caught = _catchTime > 0 ? Math.Clamp((_sinceEntry - _catchTime) / CatchFade, 0, 1) : 1;
        _sinceShift += seconds;
        // First put in at a stand has no clutch out, and is not heard.
        bool selected = clutchLeft == 0 && speed < HavokStandIn.StandingSpeed;
        if (gear != _gear && !selected)
        {
            _pendingShift = gear > _gear ? 1 : -1;
            // The stand-in changed gear this step, so none of its clutch time has gone yet.
            _shiftTime = clutchLeft;
            _sinceShift = 0;
        }
        _gear = gear;
        if (_pendingShift != 0 && _sinceShift >= 0.5f * _shiftTime)
        {
            heard = _pendingShift;
            _pendingShift = 0;
        }
        Lifted = _sinceShift < _shiftTime;

        _clock += seconds;
        double turn = 2 * Math.PI * _clock;
        float wander = (float)(0.6 * Math.Sin(turn / WanderPeriods[0]) + 0.4 * Math.Sin(turn / WanderPeriods[1] + 1.3));

        float range = Redline - Idle;
        float wheels = Idle + (Math.Abs(havokRpm) - HavokStandIn.MinRpm) * range / (HavokStandIn.UpshiftRpm - HavokStandIn.MinRpm);
        float target = Math.Max(Idle, wheels);
        float slip = Idle + (ClutchSlip + SlipWander * wander) * range;
        if (wheels < slip)
        {
            target = Math.Max(target, Idle + throttle * (slip - Idle));
        }
        target = Math.Min(target, Redline);

        Rpm += Math.Clamp(target - Rpm, -FallRate * range * seconds, RiseRate * range * seconds);
        return heard;
    }
}
