using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>
/// What the gizmos are drawn with: each handle as a list of triangle corners built round +Z, which
/// <see cref="TranslateGizmo.Orientation"/> turns onto each axis, and the colours of the axes.
/// </summary>
public static class GizmoGeometry
{
    /// <summary>How opaque a plane square is, idle and held.</summary>
    public const float PlaneAlpha = 0.35f, HeldPlaneAlpha = 0.75f;

    // Where the shaft starts and stops, and where the head widens, along a unit arm.
    private const float ShaftStart = 0.08f, ShaftEnd = 0.76f, ShaftRadius = 0.018f, HeadRadius = 0.07f;
    private const int Sides = 10;
    private const int RingSegments = 64;

    public static Vector3 Highlight { get; } = new(1f, 0.92f, 0.4f);

    /// <summary>An arm's colour; a plane square takes the mean of its two arms'.</summary>
    public static Vector3 Tint(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => new Vector3(0.92f, 0.29f, 0.31f),
        GizmoAxis.Y => new Vector3(0.42f, 0.83f, 0.31f),
        GizmoAxis.Z => new Vector3(0.32f, 0.55f, 0.98f),
        _ => (Tint(TranslateGizmo.ArmsOf(axis).U) + Tint(TranslateGizmo.ArmsOf(axis).V)) * 0.5f,
    };

    /// <summary>The move gizmo's arm: a shaft, a cone and the cap under it, along +Z of unit length.</summary>
    public static Vector3[] Arrow()
    {
        var corners = new List<Vector3>();
        for (int side = 0; side < Sides; side++)
        {
            (float cosA, float sinA) = Turn(side, Sides);
            (float cosB, float sinB) = Turn(side + 1, Sides);

            Vector3 shaftA = new(cosA * ShaftRadius, sinA * ShaftRadius, 0f);
            Vector3 shaftB = new(cosB * ShaftRadius, sinB * ShaftRadius, 0f);
            corners.AddRange([shaftA + Along(ShaftStart), shaftB + Along(ShaftStart), shaftB + Along(ShaftEnd)]);
            corners.AddRange([shaftA + Along(ShaftStart), shaftB + Along(ShaftEnd), shaftA + Along(ShaftEnd)]);

            Vector3 headA = new(cosA * HeadRadius, sinA * HeadRadius, 0f);
            Vector3 headB = new(cosB * HeadRadius, sinB * HeadRadius, 0f);
            corners.AddRange([headA + Along(ShaftEnd), headB + Along(ShaftEnd), Along(1f)]);
            corners.AddRange([headB + Along(ShaftEnd), headA + Along(ShaftEnd), Along(ShaftEnd)]);
        }
        return [.. corners];
    }

    /// <summary>The move gizmo's square between two arms, in the XY plane.</summary>
    public static Vector3[] Square()
    {
        const float a = TranslateGizmo.PlaneStart, b = TranslateGizmo.PlaneEnd;
        return [new(a, a, 0f), new(b, a, 0f), new(b, b, 0f), new(a, a, 0f), new(b, b, 0f), new(a, b, 0f)];
    }

    /// <summary>
    /// The rotate gizmo's ring: a thin tube round the unit circle square to +Z, in the same basis
    /// <see cref="RotateGizmo"/> measures its angles in.
    /// </summary>
    public static Vector3[] Ring()
    {
        var corners = new List<Vector3>();
        for (int segment = 0; segment < RingSegments; segment++)
        {
            (float cosA, float sinA) = Turn(segment, RingSegments);
            (float cosB, float sinB) = Turn(segment + 1, RingSegments);
            for (int side = 0; side < Sides; side++)
            {
                (float cosS, float sinS) = Turn(side, Sides);
                (float cosT, float sinT) = Turn(side + 1, Sides);
                Vector3 a0 = OnTube(cosA, sinA, cosS, sinS), a1 = OnTube(cosA, sinA, cosT, sinT);
                Vector3 b0 = OnTube(cosB, sinB, cosS, sinS), b1 = OnTube(cosB, sinB, cosT, sinT);
                corners.AddRange([a0, b0, b1, a0, b1, a1]);
            }
        }
        return [.. corners];

        static Vector3 OnTube(float cosRing, float sinRing, float cosTube, float sinTube)
        {
            float reach = 1f + (cosTube * ShaftRadius);
            return new Vector3(cosRing * reach, sinRing * reach, sinTube * ShaftRadius);
        }
    }

    private static (float Cos, float Sin) Turn(int step, int steps)
    {
        float angle = MathF.Tau * step / steps;
        return (MathF.Cos(angle), MathF.Sin(angle));
    }

    private static Vector3 Along(float distance) => new(0f, 0f, distance);
}
