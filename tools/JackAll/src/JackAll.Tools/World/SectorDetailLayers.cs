using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// Which terrain layers each sector blends: <c>sector&lt;id&gt;.desc.fcb</c>'s <c>DetailTexMask</c>,
/// four layer-table indices packed into a <c>u32</c> with <c>0xFF</c> for an unused slot.
/// </summary>
/// <remarks>
/// Shipped sectors leave the LOW byte unused far more often than the high one, and a sector that
/// blends rock commonly spends two slots on the same texture's X and Y projections.
/// </remarks>
public sealed class SectorDetailLayers
{
    /// <summary>Four layer indices per sector id, row-major over the map's sector grid.</summary>
    public byte[] Indices { get; }

    public int SectorsPerSide { get; }

    private SectorDetailLayers(byte[] indices, int sectorsPerSide)
    {
        Indices = indices;
        SectorsPerSide = sectorsPerSide;
    }

    public static SectorDetailLayers Load(TerrainMap map, Func<string, byte[]?> readByPath)
    {
        uint detailTexMask = FcbClassDefinitions.Crc32Ascii("DetailTexMask");
        int sectors = map.SectorsPerSide * map.SectorsPerSide;
        var indices = new byte[sectors * 4];
        Array.Fill(indices, (byte)0xFF);

        Parallel.ForEach(map.Sectors, item =>
        {
            // The descriptor sits beside the terrain, one folder over.
            string path = item.Path
                .Replace(@"\sdat\", @"\worldsectors\", StringComparison.OrdinalIgnoreCase)
                .Replace($"sd{item.SectorId}.sdat", $"sector{item.SectorId}.desc.fcb", StringComparison.OrdinalIgnoreCase);
            if (readByPath(path) is not { } bytes)
            {
                return;
            }

            uint? mask;
            try
            {
                mask = Find(FcbDocument.Deserialize(bytes), detailTexMask);
            }
            catch (InvalidDataException)
            {
                return;
            }
            if (mask is null)
            {
                return;
            }

            for (int slot = 0; slot < 4; slot++)
            {
                indices[item.SectorId * 4 + slot] = (byte)(mask.Value >> (slot * 8) & 0xFF);
            }
        });

        return new SectorDetailLayers(indices, map.SectorsPerSide);
    }

    private static uint? Find(FcbObject node, uint valueHash)
    {
        if (node.Values.TryGetValue(valueHash, out byte[]? value) && value.Length >= 4)
        {
            return BitConverter.ToUInt32(value, 0);
        }
        foreach (FcbObject child in node.Children)
        {
            if (Find(child, valueHash) is { } found)
            {
                return found;
            }
        }
        return null;
    }
}
