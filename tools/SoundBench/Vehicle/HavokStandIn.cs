using SoundBench.Game;

namespace SoundBench.Vehicle;

/// <summary>
/// Havok's engine and gearbox with FC2's constants, driving a car on flat ground with no wheelspin: the
/// revs, gear and speed the vehicle overhaul reads from the physics. The engine's resistance off the pedal
/// is approximated.
/// </summary>
public sealed class HavokStandIn
{
    public const float MinRpm = 1000, OptRpm = 5500, MaxRpm = 7500, UpshiftRpm = 6500, DownshiftRpm = 3500;

    private const float WheelRadius = 0.4f, Gravity = 9.81f, MetresPerSecondPerMph = 0.44704f;
    private const float GroundFriction = 0.8f, TyreFriction = 3.0f, RollResist = 0.1f;
    private const float Drag = 0.5f * 1.3f * 1.0f * 0.7f;
    private const float RpmPerRadianPerSecond = 60 / (2 * MathF.PI);

    private readonly float[] _gears;
    private readonly float _primaryRatio, _mass, _maxTorque, _brakeTorque, _grip, _rolling;
    private float _clutch;

    public HavokStandIn(CarPhysics car, OverhaulTuning tuning)
    {
        _gears = GearRatios(Math.Max(1, car.Gears));
        float topSpeed = Math.Max(1, car.TopSpeedMph) * MetresPerSecondPerMph;
        _primaryRatio = MaxRpm / RpmPerRadianPerSecond * WheelRadius / (topSpeed * _gears[^1]) / tuning.TopSpeed;
        _mass = Math.Max(1, car.Mass);
        _maxTorque = car.EnginePower * tuning.EnginePower;
        _brakeTorque = car.BrakingTorque * tuning.BrakeTorque;
        _grip = TyreFriction * GroundFriction * tuning.TyreGrip * Gravity;
        _rolling = RollResist * tuning.RollingResistance * Gravity;
    }

    /// <summary>Metres per second, forwards only.</summary>
    public float Speed { get; private set; }

    /// <summary>0 is first gear.</summary>
    public int Gear { get; private set; }

    public float Rpm { get; private set; }

    public bool ClutchOut => _clutch > 0;

    /// <param name="throttle">0..1.</param>
    /// <param name="brake">0..1.</param>
    /// <param name="clutchTime">How long a gear change cuts the drive: the overhaul's shift time.</param>
    /// <param name="holdGear">Keeps the gear it is in, whatever the revs.</param>
    public void Step(float seconds, float throttle, float brake, float clutchTime, bool holdGear)
    {
        Rpm = WheelRpm(Gear);
        if (_clutch > 0)
        {
            _clutch -= seconds;
        }
        else if (!holdGear)
        {
            if (Rpm > UpshiftRpm && Gear < _gears.Length - 1)
            {
                Shift(+1, clutchTime);
            }
            else if (Rpm < DownshiftRpm && Gear > 0)
            {
                Shift(-1, clutchTime);
            }
        }

        // The vehicle overhaul's rev limiter cuts the drive at the shift point, which it lays over the redline.
        float drive = _clutch > 0 || Rpm >= UpshiftRpm ? 0 : EngineTorque(throttle) * _gears[Gear] * _primaryRatio / WheelRadius / _mass;
        float braking = Math.Min(brake * _brakeTorque / WheelRadius / _mass, _grip);
        float resistance = Drag * Speed * Speed / _mass + (Speed > 1 ? _rolling : 0);
        Speed = Math.Max(0, Speed + (Math.Clamp(drive, -_grip, _grip) - braking - resistance) * seconds);
    }

    private void Shift(int by, float clutchTime)
    {
        Gear += by;
        _clutch = clutchTime;
        Rpm = WheelRpm(Gear);
    }

    private float WheelRpm(int gear) => Speed / WheelRadius * _gears[gear] * _primaryRatio * RpmPerRadianPerSecond;

    /// <summary>The pedal's share of the peak torque along the torque curve, less the engine's resistance
    /// for the share off the pedal.</summary>
    private float EngineTorque(float throttle)
    {
        float rpm = Math.Clamp(Rpm, MinRpm, MaxRpm);
        return _maxTorque * (throttle * AlongCurve(rpm, 0.8f, 1f, 0f) - (1 - throttle) * AlongCurve(rpm, 0.05f, 0.1f, 0f));
    }

    private static float AlongCurve(float rpm, float atMin, float atOpt, float atMax) => rpm < OptRpm
        ? atMin + (atOpt - atMin) * (rpm - MinRpm) / (OptRpm - MinRpm)
        : atOpt + (atMax - atOpt) * (rpm - OptRpm) / (MaxRpm - OptRpm);

    /// <summary>1.5, 1.2, 0.9, 0.75, then each 0.75 of the last.</summary>
    private static float[] GearRatios(int count)
    {
        float[] ratios = new float[count];
        float[] first = [1.5f, 1.2f, 0.9f, 0.75f];
        for (int i = 0; i < count; i++)
        {
            ratios[i] = i < first.Length ? first[i] : ratios[i - 1] * 0.75f;
        }
        return ratios;
    }
}
