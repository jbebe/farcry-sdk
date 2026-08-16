using System.Text.RegularExpressions;
using JackAll.Tools.Sdat;

namespace JackAll.Tools.World;

/// <summary>
/// One loadable map's terrain: every <c>sd&lt;id&gt;.sdat</c> that shares a sector-id space, plus the
/// square sector grid those ids index. A single-player world spans 25 level folders on one 80x80
/// grid; a multiplayer map or <c>tmpla</c> is one folder on its own 10x10 or 8x8 grid.
/// </summary>
/// <remarks>
/// Maps are discovered from a curated path list, never by probing synthesized paths against the
/// hash-only archive index - CRC32 collisions with unrelated entries are real. <c>ige_map</c> ships
/// no <c>.sdat</c> at all (the editor keeps its terrain inside <c>.fc2map</c> documents), so it
/// never appears.
/// </remarks>
public sealed partial class TerrainMap
{
    /// <summary>The largest grid any shipped map uses; caps the field a stray sector id can demand.</summary>
    private const int MaxSectorsPerSide = 80;

    public required string Name { get; init; }

    public required int SectorsPerSide { get; init; }

    public required IReadOnlyList<(string Path, int SectorId)> Sectors { get; init; }

    /// <summary>Height samples a side once the sectors are stitched, neighbours sharing their edge.</summary>
    public int GridSide => SdatTerrainCrop.GridSideFor(SectorsPerSide);

    [GeneratedRegex(@"^levels\\(?<level>[^\\]+)\\generated\\sdat\\sd(?<id>\d+)\.sdat$", RegexOptions.IgnoreCase)]
    private static partial Regex TerrainPattern();

    /// <summary>The single-player level cells, which share one world-wide sector grid.</summary>
    [GeneratedRegex(@"^w(?<digit>[12])_[a-e]_[1-5]$", RegexOptions.IgnoreCase)]
    private static partial Regex SpCellPattern();

    public static IReadOnlyList<TerrainMap> Discover(IEnumerable<string> candidatePaths)
    {
        var byName = new Dictionary<string, List<(string Path, int SectorId)>>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in candidatePaths)
        {
            // Cheap prefilter - the overwhelming majority of the ~700k VFS paths are not level
            // assets and should never reach the regex engine.
            if (!path.StartsWith(@"levels\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Match terrain = TerrainPattern().Match(path);
            if (!terrain.Success)
            {
                continue;
            }

            string level = terrain.Groups["level"].Value.ToLowerInvariant();
            Match cell = SpCellPattern().Match(level);
            string name = cell.Success ? $"world{cell.Groups["digit"].Value}" : level;
            if (!byName.TryGetValue(name, out List<(string, int)>? sectors))
            {
                byName[name] = sectors = [];
            }
            sectors.Add((path, int.Parse(terrain.Groups["id"].Value)));
        }

        var maps = new List<TerrainMap>();
        foreach ((string name, List<(string Path, int SectorId)> sectors) in byName)
        {
            int side = SectorsPerSideFor(sectors.Max(s => s.SectorId));
            maps.Add(new TerrainMap
            {
                Name = name,
                SectorsPerSide = side,
                Sectors = sectors.Where(s => s.SectorId < side * side).ToList(),
            });
        }

        // Campaign worlds first, then the rest alphabetically - the order the picker offers them.
        maps.Sort((a, b) => a.IsCampaign == b.IsCampaign
            ? string.CompareOrdinal(a.Name, b.Name)
            : a.IsCampaign ? -1 : 1);
        return maps;
    }

    private bool IsCampaign => Name.StartsWith("world", StringComparison.Ordinal);

    /// <summary>
    /// The grid side a map's ids imply. Sector ids are dense over a square grid, so the largest id
    /// gives the side directly: 6399 -> 80 (campaign), 99 -> 10 (multiplayer), 63 -> 8 (tmpla).
    /// </summary>
    private static int SectorsPerSideFor(int maxSectorId)
        => Math.Min((int)Math.Ceiling(Math.Sqrt(maxSectorId + 1)), MaxSectorsPerSide);
}
