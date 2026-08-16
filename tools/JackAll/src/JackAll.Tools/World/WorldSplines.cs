using System.Numerics;
using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.World;

/// <summary>
/// The roads, rivers and foot paths a world declares. All three are spline sets in the world's
/// <c>mapsdata.fcb</c>, grouped one set per level cell, and each spline is a list of control points
/// carrying a position, a tangent and a pair of widths. Only the control points are authored - the
/// per-segment bounding spheres and arc-length table beside them are derived.
/// </summary>
/// <remarks>See docs/docs/file-formats/splines.md.</remarks>
public static class WorldSplines
{
    private static readonly (string Kind, uint Hash)[] Containers =
    [
        ("road", FcbClassDefinitions.Crc32Ascii("RoadSplines")),
        ("river", FcbClassDefinitions.Crc32Ascii("RiverSplines")),
        ("path", FcbClassDefinitions.Crc32Ascii("PathSplines")),
    ];

    private static readonly uint Splines = FcbClassDefinitions.Crc32Ascii("Splines");
    private static readonly uint Spline = FcbClassDefinitions.Crc32Ascii("Spline");
    private static readonly uint ControlPoints = FcbClassDefinitions.Crc32Ascii("ControlPoints");
    private static readonly uint Position = FcbClassDefinitions.Crc32Ascii("Position");

    public static IReadOnlyList<WorldShape> Load(string mapName, Func<string, byte[]?> readByPath)
    {
        if (readByPath($@"worlds\{mapName}\generated\{mapName}.mapsdata.fcb") is not { } bytes)
        {
            return [];
        }

        FcbObject root;
        try
        {
            root = FcbDocument.Deserialize(bytes);
        }
        catch (InvalidDataException)
        {
            return [];
        }

        var results = new List<WorldShape>();
        foreach ((string kind, uint hash) in Containers)
        {
            var containers = new List<FcbObject>();
            Collect(root, hash, containers, 0);
            foreach (FcbObject container in containers)
            {
                AddSplines(kind, container, results);
            }
        }
        return results;
    }

    private static void AddSplines(string kind, FcbObject container, List<WorldShape> into)
    {
        foreach (FcbObject set in container.Children.Where(c => c.TypeHash == Splines))
        {
            foreach (FcbObject spline in set.Children.Where(c => c.TypeHash == Spline))
            {
                FcbObject? points = spline.Children.FirstOrDefault(c => c.TypeHash == ControlPoints);
                if (points is null)
                {
                    continue;
                }

                var positions = new List<Vector3>(points.Children.Count);
                foreach (FcbObject point in points.Children)
                {
                    if (point.Values.TryGetValue(Position, out byte[]? raw) && raw.Length >= 12)
                    {
                        positions.Add(new Vector3(
                            BitConverter.ToSingle(raw, 0),
                            BitConverter.ToSingle(raw, 4),
                            BitConverter.ToSingle(raw, 8)));
                    }
                }

                if (positions.Count > 1)
                {
                    into.Add(new WorldShape(kind, kind, "Spline", positions));
                }
            }
        }
    }

    private static void Collect(FcbObject node, uint typeHash, List<FcbObject> into, int depth)
    {
        if (node.TypeHash == typeHash)
        {
            into.Add(node);
        }
        if (depth > 12)
        {
            return;
        }
        foreach (FcbObject child in node.Children)
        {
            Collect(child, typeHash, into, depth + 1);
        }
    }
}
