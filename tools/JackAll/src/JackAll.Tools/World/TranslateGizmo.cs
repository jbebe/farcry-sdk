using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>Which handle of a gizmo a drag holds: an arm, or on the move gizmo one of the squares
/// between two arms.</summary>
public enum GizmoAxis
{
    None,
    X,
    Y,
    Z,
    PlaneXY,
    PlaneXZ,
    PlaneYZ,
}

/// <summary>A grab in progress: the handle held, where the entity stood when it was grabbed, and
/// where on the handle the grab landed, relative to that spot.</summary>
/// <remarks>The handle stays anchored where the entity was, not where it now is. Re-anchoring it
/// every frame feeds the entity's own movement back into the solve, which walks it off under the
/// cursor.</remarks>
public readonly record struct GizmoGrab(GizmoAxis Axis, Vector3 Origin, Vector3 Grip);

/// <summary>
/// The move gizmo's arithmetic: which handle a click lands on, and where the cursor has since
/// dragged it. Drawing and screen-space concerns stay in the viewport, so this can be tested
/// without a GL context.
/// </summary>
public static class TranslateGizmo
{
    /// <summary>How near an arm the ray must pass to grab it, as a fraction of the gizmo's scale.</summary>
    public const float GrabRadius = 0.13f;

    /// <summary>Where each plane square starts and stops along both of its arms, as a fraction of
    /// the gizmo's scale.</summary>
    public const float PlaneStart = 0.25f;
    public const float PlaneEnd = 0.45f;

    /// <summary>Below this the ray and the arm are near enough to parallel that the solve is
    /// unstable - the pair of closest points slides wildly for a pixel of cursor movement.</summary>
    private const float MinSeparation = 1e-4f;

    /// <summary>Below this the ray runs along a plane and the crossing point is unstable.</summary>
    private const float MinSlant = 0.05f;

    public static IReadOnlyList<GizmoAxis> Axes { get; } = [GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z];

    public static IReadOnlyList<GizmoAxis> Planes { get; } = [GizmoAxis.PlaneXY, GizmoAxis.PlaneXZ, GizmoAxis.PlaneYZ];

    public static Vector3 Direction(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => Vector3.UnitX,
        GizmoAxis.Y => Vector3.UnitY,
        GizmoAxis.Z => Vector3.UnitZ,
        _ => Vector3.Zero,
    };

    /// <summary>The two arms a plane square spans.</summary>
    public static (GizmoAxis U, GizmoAxis V) ArmsOf(GizmoAxis plane) => plane switch
    {
        GizmoAxis.PlaneXY => (GizmoAxis.X, GizmoAxis.Y),
        GizmoAxis.PlaneXZ => (GizmoAxis.X, GizmoAxis.Z),
        GizmoAxis.PlaneYZ => (GizmoAxis.Y, GizmoAxis.Z),
        _ => (GizmoAxis.None, GizmoAxis.None),
    };

    public static bool IsPlane(GizmoAxis axis) => axis >= GizmoAxis.PlaneXY;

    /// <summary>
    /// Turns a model built along +Z onto <paramref name="axis"/>. A basis rather than composed
    /// rotations, so the arm lands on the axis named rather than one sign away from it.
    /// </summary>
    public static Matrix4x4 Orientation(GizmoAxis axis)
    {
        Vector3 arm = Direction(axis);
        Vector3 aside = MathF.Abs(arm.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 across = Vector3.Normalize(Vector3.Cross(aside, arm));
        Vector3 up = Vector3.Cross(arm, across);
        return Basis(across, up, arm);
    }

    /// <summary>Carries a model drawn in the XY plane onto a plane square's two arms.</summary>
    public static Matrix4x4 PlaneOrientation(GizmoAxis plane)
    {
        (GizmoAxis u, GizmoAxis v) = ArmsOf(plane);
        Vector3 a = Direction(u), b = Direction(v);
        return Basis(a, b, Vector3.Cross(a, b));
    }

    /// <summary>
    /// The world length that holds the gizmo at <paramref name="pixels"/> of viewport height however
    /// far away the entity is.
    /// </summary>
    public static float Scale(
        Vector3 origin, Vector3 camera, float verticalFovRadians, float viewportPixels, float pixels)
        => Vector3.Distance(origin, camera)
            * 2f * MathF.Tan(verticalFovRadians * 0.5f)
            * (pixels / MathF.Max(viewportPixels, 1f));

    /// <summary>
    /// The handle the ray grabs, or null when it misses them all. A square is hit exactly while an
    /// arm is hit within a tolerance, so a ray inside a square takes it even past an arm. Otherwise
    /// the nearer handle wins, which makes the arm pointing at the camera the hard one to grab rather
    /// than the one that silently steals every click.
    /// </summary>
    public static GizmoGrab? Grab(
        Vector3 rayOrigin, Vector3 rayDirection, Vector3 origin, float scale)
    {
        GizmoGrab? grabbed = null;
        float nearest = float.MaxValue;
        foreach (GizmoAxis plane in Planes)
        {
            (GizmoAxis u, GizmoAxis v) = ArmsOf(plane);
            if (Crossing(rayOrigin, rayDirection, origin, NormalOf(plane)) is not { } t || t >= nearest)
            {
                continue;
            }

            Vector3 grip = rayOrigin + (rayDirection * t) - origin;
            if (InSquare(Vector3.Dot(grip, Direction(u)), scale) && InSquare(Vector3.Dot(grip, Direction(v)), scale))
            {
                nearest = t;
                grabbed = new GizmoGrab(plane, origin, grip);
            }
        }
        if (grabbed is not null)
        {
            return grabbed;
        }

        foreach (GizmoAxis axis in Axes)
        {
            Vector3 arm = Direction(axis);
            if (ClosestApproach(rayOrigin, rayDirection, origin, arm) is not { } approach
                || approach.Ray < 0f || approach.Ray >= nearest
                || approach.Arm < 0f || approach.Arm > scale)
            {
                continue;
            }

            float miss = Vector3.Distance(
                rayOrigin + (rayDirection * approach.Ray), origin + (arm * approach.Arm));
            if (miss <= GrabRadius * scale)
            {
                nearest = approach.Ray;
                grabbed = new GizmoGrab(axis, origin, arm * approach.Arm);
            }
        }
        return grabbed;
    }

    /// <summary>Where the entity stands with the cursor here, or null while the handle is too near
    /// edge-on to solve against - which holds the entity still instead of flinging it.</summary>
    public static Vector3? Follow(GizmoGrab grab, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (IsPlane(grab.Axis))
        {
            return Crossing(rayOrigin, rayDirection, grab.Origin, NormalOf(grab.Axis)) is { } t
                ? rayOrigin + (rayDirection * t) - grab.Grip
                : null;
        }

        Vector3 arm = Direction(grab.Axis);
        return ClosestApproach(rayOrigin, rayDirection, grab.Origin, arm) is { } approach
            ? grab.Origin + (arm * approach.Arm) - grab.Grip
            : null;
    }

    /// <summary>Where along the ray it crosses the plane through <paramref name="origin"/>, or null
    /// when that is behind the eye or the ray runs too nearly along the plane.</summary>
    public static float? Crossing(Vector3 rayOrigin, Vector3 rayDirection, Vector3 origin, Vector3 normal)
    {
        float slant = Vector3.Dot(rayDirection, normal);
        if (MathF.Abs(slant) < MinSlant)
        {
            return null;
        }

        float t = Vector3.Dot(origin - rayOrigin, normal) / slant;
        return t >= 0f ? t : null;
    }

    private static Vector3 NormalOf(GizmoAxis plane)
    {
        (GizmoAxis u, GizmoAxis v) = ArmsOf(plane);
        return Vector3.Cross(Direction(u), Direction(v));
    }

    private static bool InSquare(float along, float scale) => along >= PlaneStart * scale && along <= PlaneEnd * scale;

    private static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z) => new(
        x.X, x.Y, x.Z, 0f,
        y.X, y.Y, y.Z, 0f,
        z.X, z.Y, z.Z, 0f,
        0f, 0f, 0f, 1f);

    /// <summary>Where the ray and the arm's line come closest, as distances along each. Null when
    /// the two are within a hair of parallel.</summary>
    private static (float Ray, float Arm)? ClosestApproach(
        Vector3 rayOrigin, Vector3 rayDirection, Vector3 armOrigin, Vector3 arm)
    {
        Vector3 between = rayOrigin - armOrigin;
        float slant = Vector3.Dot(rayDirection, arm);
        float separation = 1f - (slant * slant);
        if (separation < MinSeparation)
        {
            return null;
        }

        float alongRay = Vector3.Dot(rayDirection, between);
        float alongArm = Vector3.Dot(arm, between);
        return (((slant * alongArm) - alongRay) / separation, (alongArm - (slant * alongRay)) / separation);
    }
}
