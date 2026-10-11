using System.Globalization;
using System.IO;

namespace SoundBench.Game;

/// <summary>The vehicle overhaul's tuning of one car that moves its engine: <c>bin\vehicle-overhaul.ini</c>,
/// with the defaults of <c>tuning.cpp</c> where the file is silent.</summary>
public sealed record OverhaulTuning(
    float EnginePower, float TopSpeed, float ShiftTime, float BrakeTorque, float TyreGrip, float RollingResistance)
{
    public const string OtherCars = "Other cars";

    public static OverhaulTuning Read(string iniPath, RealVehicle? real)
    {
        string section = real?.Name ?? OtherCars;
        var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(iniPath))
        {
            string current = "";
            foreach (string raw in File.ReadLines(iniPath))
            {
                string line = raw.Trim();
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    current = line[1..^1];
                }
                else if (current.Equals(section, StringComparison.OrdinalIgnoreCase)
                         && line.Split('=', 2) is [var key, var value]
                         && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                {
                    values[key.Trim()] = number;
                }
            }
        }

        return new OverhaulTuning(
            values.GetValueOrDefault("Engine power", 0.7f),
            values.GetValueOrDefault("Top speed", real?.TopSpeed > 0 ? real.TopSpeed : 2f),
            values.GetValueOrDefault("Shift time", 0.6f),
            values.GetValueOrDefault("Brake torque", 0.3f),
            values.GetValueOrDefault("Tyre grip", 0.3f),
            values.GetValueOrDefault("Rolling resistance", 0.15f));
    }
}
