using JackAll.Cli.Infrastructure;
using JackAll.Core.Mods;

namespace JackAll.Cli.Commands.World;

/// <summary>A mod layer written to disk: game files under <c>mods\</c>, the way every layer reader expects.</summary>
internal static class LayerFolder
{
    /// <summary>Refuses a folder that already holds anything, so a write never mixes with what was there.</summary>
    public static void RequireEmpty(string folder)
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            throw new IOException($"{folder} is not empty; write the layer to a new or empty folder.");
        }
    }

    public static void Write(string folder, IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        foreach ((string path, byte[] bytes) in files)
        {
            CliIO.WriteOutput(Path.Combine(folder, ModPathHashing.ModsFolder, path), bytes);
        }
    }
}
