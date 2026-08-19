using System.Numerics;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>One authored polyline. <paramref name="Kind"/> is what it is for - the renderer colours
/// by it, and it is the only thing distinguishing a road from a river or a zone outline.</summary>
public sealed record WorldShape(string Kind, string Name, string Owner, IReadOnlyList<Vector3> Points);

/// <summary>
/// The <c>hidShapePoints</c> polylines a world declares. Like the road splines these live in the
/// world's <c>mapsdata.fcb</c> rather than in any sector file, so nothing in the per-sector entity
/// pool carries them.
/// </summary>
public static class WorldShapes
{
    private static readonly uint ShapePoints = FcbClassDefinitions.Crc32Ascii("hidShapePoints");

    public static IReadOnlyList<WorldShape> Load(
        string mapName, Func<string, byte[]?> readByPath, FcbClassDefinitions definitions)
    {
        if (readByPath($@"worlds\{mapName}\generated\{mapName}.mapsdata.fcb") is not { } bytes
            || FcbDocument.TryDeserialize(bytes) is not { } root)
        {
            return [];
        }

        var shapes = new List<WorldShape>();
        Collect(root, definitions, "", shapes, 0);
        return shapes;
    }

    private static void Collect(
        FcbObject node, FcbClassDefinitions definitions, string entityName, List<WorldShape> shapes, int depth)
    {
        string name = node.TypeHash == WorldHashes.Entity
            ? FcbEntityFields.ReadString(node, WorldHashes.HidName)
            : entityName;

        if (node.Values.TryGetValue(ShapePoints, out byte[]? raw) && Decode(raw) is { Count: > 1 } points)
        {
            string owner = definitions.GetClass(node.TypeHash).Name ?? $"{node.TypeHash:X8}";
            shapes.Add(new WorldShape(
                owner.Contains("Sound", StringComparison.OrdinalIgnoreCase) ? "sound" : "shape",
                name.Length > 0 ? name : "unnamed",
                owner,
                points));
        }

        if (depth > 12)
        {
            return;
        }
        foreach (FcbObject child in node.Children)
        {
            Collect(child, definitions, name, shapes, depth + 1);
        }
    }

    /// <summary>A <c>u32</c> point count followed by that many <c>Vector3</c>s.</summary>
    private static List<Vector3>? Decode(byte[] raw)
    {
        if (raw.Length < 4)
        {
            return null;
        }

        int count = BitConverter.ToInt32(raw, 0);
        if (count <= 0 || 4 + count * 12 != raw.Length)
        {
            return null;
        }

        var points = new List<Vector3>(count);
        for (int i = 0; i < count; i++)
        {
            int at = 4 + i * 12;
            points.Add(new Vector3(
                BitConverter.ToSingle(raw, at),
                BitConverter.ToSingle(raw, at + 4),
                BitConverter.ToSingle(raw, at + 8)));
        }
        return points;
    }
}
