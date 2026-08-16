using System.Text.RegularExpressions;

namespace JackAll.Tools.World;

/// <summary>
/// One world's asset paths, classified out of the merged filesystem's full path list in a single
/// pass. This is the only place that selects world files: candidates must come from a curated
/// source (VFS/hashlist names), never from probing synthesized paths against the hash-only archive
/// index — CRC32 collisions with unrelated entries are real.
/// </summary>
public sealed partial class WorldPaths
{
    public required string WorldName { get; init; }

    /// <summary>Every <c>worldsector&lt;id&gt;.data.fcb</c> belonging to the world.</summary>
    public required IReadOnlyList<(string Path, string Cell, int SectorId)> Sectors { get; init; }

    /// <summary>Every in-grid <c>sd&lt;id&gt;.sdat</c> belonging to the world.</summary>
    public required IReadOnlyList<(string Path, int SectorId)> Terrain { get; init; }

    /// <summary>Matches an SP worldsector path and captures its cell, world digit and sector id.</summary>
    [GeneratedRegex(@"^levels\\(?<cell>w(?<world>[12])_[a-e]_[1-5])\\generated\\worldsectors\\worldsector(?<id>\d+)\.data\.fcb$",
        RegexOptions.IgnoreCase)]
    public static partial Regex SectorPattern();

    [GeneratedRegex(@"^levels\\w(?<world>[12])_[a-e]_[1-5]\\generated\\sdat\\sd(?<id>\d+)\.sdat$",
        RegexOptions.IgnoreCase)]
    public static partial Regex TerrainPattern();

    public static WorldPaths ForWorld(string worldName, IEnumerable<string> candidatePaths)
    {
        string digit = Fc2WorldGrid.WorldDigit(worldName);
        var sectors = new List<(string, string, int)>();
        var terrain = new List<(string, int)>();

        foreach (string path in candidatePaths)
        {
            // Cheap prefilter - the overwhelming majority of the ~200k VFS paths are not level
            // assets and should never reach the regex engine.
            if (!path.StartsWith(@"levels\w", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Match match = SectorPattern().Match(path);
            if (match.Success)
            {
                if (match.Groups["world"].Value == digit)
                {
                    sectors.Add((path, match.Groups["cell"].Value.ToLowerInvariant(),
                        int.Parse(match.Groups["id"].Value)));
                }
                continue;
            }

            match = TerrainPattern().Match(path);
            if (match.Success && match.Groups["world"].Value == digit)
            {
                int id = int.Parse(match.Groups["id"].Value);
                if (id < Fc2WorldGrid.WorldSide * Fc2WorldGrid.WorldSide)
                {
                    terrain.Add((path, id));
                }
            }
        }

        return new WorldPaths { WorldName = worldName, Sectors = sectors, Terrain = terrain };
    }
}
