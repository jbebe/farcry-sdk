using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace SoundBench.Dare;

/// <summary>
/// The WAVs that stand in for bank audio: each file whose name carries an audio record's id in brackets, as
/// <c>rpm-2 [0045cb51].wav</c>, which the vehicle overhaul's build scripts write beside their takes.
/// </summary>
public static partial class WavSources
{
    [GeneratedRegex(@"\[([0-9a-fA-F]{8})\]")]
    private static partial Regex Tag();

    /// <summary>The tagged WAVs under <paramref name="folders"/>, by the audio record each stands for.</summary>
    public static IReadOnlyDictionary<uint, string> In(IEnumerable<string> folders)
    {
        Dictionary<uint, string> found = [];
        foreach (string path in folders.Where(Directory.Exists).SelectMany(f => Directory.EnumerateFiles(f, "*.wav", SearchOption.AllDirectories)))
        {
            if (Tag().Match(Path.GetFileName(path)) is { Success: true } tag)
            {
                found.TryAdd(uint.Parse(tag.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture), path);
            }
        }
        return found;
    }
}
