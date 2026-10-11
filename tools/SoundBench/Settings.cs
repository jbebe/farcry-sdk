using System.IO;
using System.Text.Json;

namespace SoundBench;

/// <summary>Where the game and the mod layers are, kept between runs.</summary>
public sealed class Settings
{
    public string GamePath { get; set; } = @"C:\Games\Far Cry 2";

    /// <summary>Layer folders read over the installed game, the last one winning.</summary>
    public List<string> Layers { get; set; } = [];

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundBench", "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? Default();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return Default();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The vehicle overhaul's layer in the repo this exe was built in, so a rebuilt bank is heard
    /// before it is deployed.</summary>
    private static Settings Default()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string layer = Path.Combine(dir.FullName, "mods", "vehicle-overhaul", "layer");
            if (Directory.Exists(layer))
            {
                return new Settings { Layers = [layer] };
            }
        }
        return new Settings();
    }
}
