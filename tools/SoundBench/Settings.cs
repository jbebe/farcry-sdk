using System.IO;
using System.Text.Json;

namespace SoundBench;

/// <summary>Where the game, the mod layers and the WAVs that stand in for bank audio are, kept between runs.</summary>
public sealed class Settings
{
    public string GamePath { get; set; } = @"C:\Games\Far Cry 2";

    /// <summary>Layer folders read over the installed game, the last one winning.</summary>
    public List<string> Layers { get; set; } = InOverhaul("layer");

    /// <summary>Folders searched for tagged WAVs; see <see cref="Dare.WavSources"/>.</summary>
    public List<string> Wavs { get; set; } = InOverhaul("assets", "vehicle-sounds");

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundBench", "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>A folder of the vehicle overhaul in the repo this exe was built in, or none.</summary>
    private static List<string> InOverhaul(params string[] path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string overhaul = Path.Combine(dir.FullName, "mods", "vehicle-overhaul");
            if (Directory.Exists(Path.Combine(overhaul, "layer")))
            {
                return [Path.Combine([overhaul, .. path])];
            }
        }
        return [];
    }
}
