using System.Globalization;
using System.IO;

namespace SoundBench.Game;

/// <summary>The vehicle overhaul's tuning of one car that moves its engine: <c>bin\vehicle-overhaul.ini</c>,
/// with the defaults of <c>tuning.cpp</c> and the car's own starting tunings where the file is silent.</summary>
public sealed record OverhaulTuning(
    float EnginePower, float TopSpeed, float ShiftTime, float DownshiftTime, float BrakeTorque, float TyreGrip,
    float RollingResistance)
{
    public const string OtherCars = "Other cars";

    private static readonly Dictionary<string, float> Defaults = new()
    {
        ["Engine power"] = 0.7f, ["Top speed"] = 2f, ["Shift time"] = 0.6f, ["Downshift time"] = 0.1f, ["Brake torque"] = 0.3f,
        ["Tyre grip"] = 0.3f, ["Rolling resistance"] = 0.15f,
    };

    public static OverhaulTuning Read(string iniPath, RealVehicle? real)
    {
        string section = real?.Name ?? OtherCars;
        var values = new Dictionary<string, float>(Defaults, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, float value) in real?.Tuning ?? new Dictionary<string, float>())
        {
            values[key] = value;
        }
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
            values["Engine power"], values["Top speed"], values["Shift time"], values["Downshift time"], values["Brake torque"],
            values["Tyre grip"], values["Rolling resistance"]);
    }
}
