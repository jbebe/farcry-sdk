using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>Which arm of the move gizmo a drag runs along.</summary>
public enum GizmoAxis
{
    None,
    X,
    Y,
    Z,
}

/// <summary>A grab in progress: the arm held, where the entity stood when it was grabbed, and how
/// far along the arm the grab landed.</summary>
/// <remarks>The arm stays anchored where the entity was, not where it now is. Re-anchoring it every
/// frame feeds the entity's own movement back into the solve, which walks it off under the cursor.
/// </remarks>
public readonly record struct GizmoGrab(GizmoAxis Axis, Vector3 Origin, float Along);

/// <summary>
/// The move gizmo's arithmetic: which arm a click lands on, and where along that arm the cursor has
/// since dragged. Drawing and screen-space concerns stay in the viewport, so this can be tested
/// without a GL context.
/// </summary>
public static class TranslateGizmo
{
    /// <summary>How near an arm the ray must pass to grab it, as a fraction of the gizmo's scale.</summary>
    public const float GrabRadius = 0.13f;

    /// <summary>Below this the ray and the arm are near enough to parallel that the solve is
    /// unstable - the pair of closest points slides wildly for a pixel of cursor movement.</summary>
    private const float MinSeparation = 1e-4f;

    public static IReadOnlyList<GizmoAxis> Axes { get; } = [GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z];

    public static Vector3 Direction(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => Vector3.UnitX,
        GizmoAxis.Y => Vector3.UnitY,
        GizmoAxis.Z => Vector3.UnitZ,
        _ => Vector3.Zero,
    };

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
        return new Matrix4x4(
            across.X, across.Y, across.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            arm.X, arm.Y, arm.Z, 0f,
            0f, 0f, 0f, 1f);
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
    /// The arm the ray grabs, or null when it misses all three. Where two arms overlap on screen the
    /// nearer one wins, which is what makes the arm pointing at the camera the hard one to grab
    /// rather than the one that silently steals every click.
    /// </summary>
    public static GizmoGrab? Grab(
        Vector3 rayOrigin, Vector3 rayDirection, Vector3 origin, float scale)
    {
        GizmoGrab? grabbed = null;
        float nearest = float.MaxValue;
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
                grabbed = new GizmoGrab(axis, origin, approach.Arm);
            }
        }

        return grabbed;
    }

    /// <summary>Where the entity stands with the cursor here, or null while the arm is too near
    /// edge-on to solve against - which holds the entity still instead of flinging it.</summary>
    public static Vector3? Follow(GizmoGrab grab, Vector3 rayOrigin, Vector3 rayDirection)
    {
        Vector3 arm = Direction(grab.Axis);
        return ClosestApproach(rayOrigin, rayDirection, grab.Origin, arm) is { } approach
            ? grab.Origin + (arm * (approach.Arm - grab.Along))
            : null;
    }

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
