using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>One walkable navmesh node: where it sits and the surface normal the engine tests its
/// slope against.</summary>
public readonly record struct NavMeshNode(Vector3 Position, Vector3 Normal);

/// <summary>
/// The AI navigation mesh a map ships, read from the per-sector <c>nv_&lt;id&gt;.nvm</c> files.
/// Only campaign levels have them, so a world loads whichever of its sectors do.
/// </summary>
/// <remarks>
/// Node positions are quantised: <c>CNavArchive::SetPackedVectorSettings</c> makes the sector's own
/// bounding box the basis, so X and Y are <c>int16</c> steps of <c>1 / (1 &lt;&lt; (15 - log2(span)))</c>
/// around its centre while Z is a plain <c>int16</c> in 1/32 metre. Decoding a node therefore needs
/// its sector's header, never the world grid.
/// </remarks>
public static class WorldNavMesh
{
    /// <summary>The per-sector tag, "hMvN" on disk.</summary>
    private const uint Magic = 0x4E764D68;

    /// <summary>Serialized size of one <c>CNavMeshNode</c>, which is smaller than its 60-byte
    /// in-memory form.</summary>
    private const int NodeSize = 48;

    /// <summary>Where a node's packed position starts, past the six fields ahead of it.</summary>
    private const int NodePositionOffset = 15;

    /// <summary>Format version that introduced this node layout; everything retail ships is newer.</summary>
    private const uint FirstKnownVersion = 0x13900;

    private const float NormalScale = 1f / 127f;
    private const float HeightStep = 1f / 32f;

    public static IReadOnlyList<NavMeshNode> Load(
        TerrainMap map, Func<string, byte[]?> readByPath, IProgress<string>? progress = null)
    {
        progress?.Report($"Loading {map.Name} navmesh");

        var perSector = new List<NavMeshNode>[map.Sectors.Count];
        Parallel.For(0, map.Sectors.Count, index =>
        {
            (string path, int sectorId) = map.Sectors[index];
            string file = path
                .Replace(@"\sdat\", @"\nv\sectors\", StringComparison.OrdinalIgnoreCase)
                .Replace($"sd{sectorId}.sdat", $"nv_{sectorId}.nvm", StringComparison.OrdinalIgnoreCase);

            if (readByPath(file) is { } bytes)
            {
                perSector[index] = ReadSector(bytes);
            }
        });

        var all = new List<NavMeshNode>(perSector.Sum(s => s?.Count ?? 0));
        int sectors = 0;
        foreach (List<NavMeshNode>? sector in perSector)
        {
            if (sector is not null)
            {
                all.AddRange(sector);
                sectors++;
            }
        }

        progress?.Report($"Loaded {map.Name} navmesh: {all.Count:N0} nodes across {sectors:N0} sectors");
        return all;
    }

    /// <summary>Reads one sector file, or returns null if it is not a navmesh this can decode.</summary>
    public static List<NavMeshNode>? ReadSector(byte[] b)
    {
        if (b.Length < 0x36 || BitConverter.ToUInt32(b, 4) != Magic ||
            BitConverter.ToUInt32(b, 8) < FirstKnownVersion)
        {
            return null;
        }

        float minX = BitConverter.ToSingle(b, 0x14), minY = BitConverter.ToSingle(b, 0x18);
        float maxX = BitConverter.ToSingle(b, 0x1C), maxY = BitConverter.ToSingle(b, 0x20);
        uint span = (uint)MathF.Round(MathF.Max(maxX - minX, maxY - minY)) * 2;
        if (!float.IsFinite(minX) || !float.IsFinite(minY) || span == 0 || (span & (span - 1)) != 0)
        {
            return null;
        }

        var centre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
        float step = 1f / (1 << (15 - BitOperations.Log2(span)));

        // field_0x6c, the two edition words, field_0x5c, then a u32-counted array the quadtree owns.
        int at = 0x24 + 4 + 8 + 2;
        at += 4 + (int)BitConverter.ToUInt32(b, at) * 4;
        if (at + 4 > b.Length)
        {
            return null;
        }

        uint count = BitConverter.ToUInt32(b, at);
        at += 4;
        if (count == 0 || at + (long)count * NodeSize > b.Length)
        {
            return null;
        }

        var nodes = new List<NavMeshNode>((int)count);
        for (uint n = 0; n < count; n++)
        {
            int p = at + (int)n * NodeSize + NodePositionOffset;
            nodes.Add(new NavMeshNode(
                new Vector3(
                    BitConverter.ToInt16(b, p) * step + centre.X,
                    BitConverter.ToInt16(b, p + 2) * step + centre.Y,
                    BitConverter.ToInt16(b, p + 4) * HeightStep),
                new Vector3(
                    (sbyte)b[p + 6] * NormalScale,
                    (sbyte)b[p + 7] * NormalScale,
                    (sbyte)b[p + 8] * NormalScale)));
        }
        return nodes;
    }
}
