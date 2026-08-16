namespace JackAll.Tools.World;

/// <summary>
/// The FC2 single-player world's sector geometry: an 80x80 grid of 64-unit sectors, split into 5x5
/// level cells of 16x16 sectors named <c>w{n}_{a-e}_{1-5}</c>. Sector ids are global
/// (<c>row * 80 + col</c>, the letter picking the row band counting DOWN from the top, so w1_a_1
/// spans ids 5120-6335); world2 additionally ships out-of-grid ids that are file identities only —
/// see docs/docs/file-formats/fc2map.md's sector-numbering section.
/// </summary>
public static class Fc2WorldGrid
{
    /// <summary>Sectors per world edge.</summary>
    public const int WorldSide = 80;

    /// <summary>Sectors per level-cell edge.</summary>
    public const int CellSide = 16;

    /// <summary>World units per sector edge.</summary>
    public const int UnitsPerSector = 64;

    /// <summary>The single-player worlds, in the order the UI offers them.</summary>
    public static readonly IReadOnlyList<string> SpWorldNames = ["world1", "world2"];

    /// <summary>The digit <paramref name="worldName"/>'s level cells carry, e.g. "world1" → "1".</summary>
    public static string WorldDigit(string worldName) => worldName switch
    {
        "world1" => "1",
        "world2" => "2",
        _ => throw new ArgumentException($"'{worldName}' is not an SP world (world1/world2).", nameof(worldName)),
    };

    public static int SectorId(int row, int col) => row * WorldSide + col;

    public static (int Row, int Col) SectorCoords(int sectorId) =>
        (sectorId / WorldSide, sectorId % WorldSide);

    /// <summary>The id of the sector containing world point (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static int SectorIdAt(float x, float y)
        => SectorId((int)(y / UnitsPerSector), (int)(x / UnitsPerSector));

    /// <summary>The level cell owning an in-grid sector, e.g. sector 2984 of "world1" → "w1_c_2" -
    /// the letter counts down from the top row band (see the class remarks).</summary>
    public static string CellNameFor(string worldName, int sectorId)
    {
        (int row, int col) = SectorCoords(sectorId);
        char letter = (char)('a' + (WorldSide / CellSide - 1) - row / CellSide);
        return $"w{WorldDigit(worldName)}_{letter}_{col / CellSide + 1}";
    }
}
