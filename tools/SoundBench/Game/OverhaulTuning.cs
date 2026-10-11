using System.Globalization;
using System.IO;

namespace SoundBench.Game;

/// <summary>The vehicle overhaul's tuning of one car that moves its engine: <c>bin\vehicle-overhaul.ini</c>,
/// with the overhaul's defaults (<c>tuning.cpp</c>) where the file is silent.</summary>
public sealed record OverhaulTuning(
    float EnginePower = 0.7f,
    float TopSpeed = 2f,
    float ShiftTime = 0.6f,
    float BrakeTorque = 0.3f,
    float TyreGrip = 0.3f,
    float RollingResistance = 0.15f)
{
    public const string OtherCars = "Other cars";

    public static OverhaulTuning Read(string iniPath, string section)
    {
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

        var defaults = new OverhaulTuning();
        float Get(string key, float fallback) => values.GetValueOrDefault(key, fallback);
        return new OverhaulTuning(
            Get("Engine power", defaults.EnginePower),
            Get("Top speed", defaults.TopSpeed),
            Get("Shift time", defaults.ShiftTime),
            Get("Brake torque", defaults.BrakeTorque),
            Get("Tyre grip", defaults.TyreGrip),
            Get("Rolling resistance", defaults.RollingResistance));
    }
}
