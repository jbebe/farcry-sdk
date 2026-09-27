using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.Ai;

/// <summary>One knot of a curve: an input, the value there, and how the segment ending on it is drawn.</summary>
public readonly record struct CurvePoint(float X, float Y, uint Type);

/// <summary>
/// A <c>CCurve</c> archetype (<c>Curves.ShootingSystem.*</c>, <c>Curves.AIWeapon.*</c>): knots in a
/// <c>curveCurve</c>. <c>CCurveObj::GetValue</c> clamps outside the first and last knot and draws a
/// segment by its end knot's type: 1 Hermite on the knots' tangents, 2 linear plus a sine, else linear.
/// </summary>
public static class AiCurve
{
    private static readonly uint CurveType = FcbClassDefinitions.Crc32Ascii("curveCurve");
    private static readonly uint KnotsType = FcbClassDefinitions.Crc32Ascii("Knots");
    private static readonly uint KnotType = FcbClassDefinitions.Crc32Ascii("Knot");
    private static readonly uint ValueField = FcbClassDefinitions.Crc32Ascii("Value");
    private static readonly uint TypeField = FcbClassDefinitions.Crc32Ascii("Type");

    public static bool IsCurve(FcbObject entity) => Knots(entity).Any();

    public static IReadOnlyList<CurvePoint> Read(FcbObject entity) =>
        [.. Knots(entity).Select(k => k.Values.GetValueOrDefault(ValueField) is { Length: >= 8 } v
            ? new CurvePoint(BitConverter.ToSingle(v, 0), BitConverter.ToSingle(v, 4), FcbEntityFields.ReadU32(k, TypeField) ?? 0)
            : default)];

    /// <summary>Moves knot <paramref name="index"/> to (<paramref name="x"/>, <paramref name="y"/>); tangents are left as they are.</summary>
    public static bool Write(FcbObject entity, int index, float x, float y)
    {
        if (Knots(entity).ElementAtOrDefault(index)?.Values.GetValueOrDefault(ValueField) is not { Length: >= 8 } value)
        {
            return false;
        }
        BitConverter.GetBytes(x).CopyTo(value, 0);
        BitConverter.GetBytes(y).CopyTo(value, 4);
        return true;
    }

    /// <summary>The curve at <paramref name="x"/> for linear segments, as the engine draws them.</summary>
    public static float Evaluate(IReadOnlyList<CurvePoint> points, float x)
    {
        if (points.Count == 0)
        {
            return 0;
        }
        if (x <= points[0].X)
        {
            return points[0].Y;
        }
        for (int i = 1; i < points.Count; i++)
        {
            if (x <= points[i].X)
            {
                CurvePoint a = points[i - 1], b = points[i];
                return b.X == a.X ? b.Y : a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
            }
        }
        return points[^1].Y;
    }

    private static IEnumerable<FcbObject> Knots(FcbObject entity) => entity.Children
        .Where(c => c.TypeHash == CurveType)
        .SelectMany(c => c.Children.Where(k => k.TypeHash == KnotsType))
        .SelectMany(k => k.Children.Where(n => n.TypeHash == KnotType));
}
