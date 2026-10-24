using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// A world's <c>sectorsdep.fcb</c>: one group named after the map, holding a <c>CWorldSector</c>
/// entry per sector whose booleans say which of the sector's files the game loads.
/// See docs/docs/modding/building-a-new-world.md#which-sector-files-the-game-loads.
/// </summary>
public static class SectorsDep
{
    public const string HasMainSectorData = "HasMainSectorData";
    public const string HasNavMesh = "HasNavMesh";

    private static readonly uint WorldSector = FcbClassDefinitions.Crc32Ascii("CWorldSector");
    private static readonly uint SectorId = FcbClassDefinitions.Crc32Ascii("SectorId");

    /// <summary>Sets <paramref name="flag"/> on these sectors' entries. Returns how many it set.</summary>
    public static int Mark(FcbObject sectorsDep, string flag, IReadOnlySet<int> sectorIds)
    {
        uint key = FcbClassDefinitions.Crc32Ascii(flag);
        int marked = 0;
        foreach (FcbObject sector in Entries(sectorsDep))
        {
            if (IdOf(sector) is int id && sectorIds.Contains(id))
            {
                sector.Values[key] = [1];
                marked++;
            }
        }
        return marked;
    }

    /// <summary>Drops every entry whose sector is not in <paramref name="sectorIds"/>.</summary>
    public static void Keep(FcbObject sectorsDep, IReadOnlySet<int> sectorIds)
    {
        foreach (FcbObject group in sectorsDep.Children)
        {
            group.Children.RemoveAll(s => s.TypeHash == WorldSector && !(IdOf(s) is int id && sectorIds.Contains(id)));
        }
    }

    private static IEnumerable<FcbObject> Entries(FcbObject sectorsDep)
        => sectorsDep.Children.SelectMany(g => g.Children).Where(s => s.TypeHash == WorldSector);

    private static int? IdOf(FcbObject sector) => (int?)FcbEntityFields.ReadU32(sector, SectorId);
}
