using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>A ring grab in progress: the axis turned about, the pivot, and where on the ring the
/// grab landed, in degrees.</summary>
public readonly record struct RotateGrab(GizmoAxis Axis, Vector3 Origin, float StartAngle);

/// <summary>
/// The rotate gizmo's arithmetic: one ring per world axis, radius the gizmo's scale. Which ring a
/// click lands on, how far round it the cursor has since dragged, and what that does to
/// <c>hidAngles</c>. The sibling of <see cref="TranslateGizmo"/>, and GL-free for the same reason.
/// </summary>
public static class RotateGizmo
{
    /// <summary>How near the ring the ray must cross its plane, as a fraction of the radius.</summary>
    public const float GrabRadius = 0.12f;

    /// <summary>Below this the ray runs along the ring's plane and the crossing point is unstable.</summary>
    private const float MinSlant = 0.05f;

    /// <summary>The ring the ray grabs, or null when it misses all three; the nearer crossing wins.</summary>
    public static RotateGrab? Grab(Vector3 rayOrigin, Vector3 rayDirection, Vector3 origin, float scale)
    {
        RotateGrab? grabbed = null;
        float nearest = float.MaxValue;
        foreach (GizmoAxis axis in TranslateGizmo.Axes)
        {
            if (Crossing(rayOrigin, rayDirection, origin, axis) is not { } t || t >= nearest)
            {
                continue;
            }

            Vector3 offset = rayOrigin + (rayDirection * t) - origin;
            if (MathF.Abs(offset.Length() - scale) <= GrabRadius * scale)
            {
                nearest = t;
                grabbed = new RotateGrab(axis, origin, AngleOnRing(axis, offset));
            }
        }
        return grabbed;
    }

    /// <summary>How far round the ring the cursor now is from where it grabbed, in degrees within
    /// ±180, or null while the ring is too near edge-on to solve.</summary>
    public static float? Follow(RotateGrab grab, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (Crossing(rayOrigin, rayDirection, grab.Origin, grab.Axis) is not { } t)
        {
            return null;
        }

        float delta = AngleOnRing(grab.Axis, rayOrigin + (rayDirection * t) - grab.Origin) - grab.StartAngle;
        return delta - (360f * MathF.Round(delta / 360f));
    }

    /// <summary>Turns an orientation by <paramref name="degrees"/> about a world axis, counter-clockwise
    /// looking down it, and returns it as angles again.</summary>
    public static Vector3 Apply(Vector3 angles, GizmoAxis axis, float degrees)
        => EulerZxy.FromMatrix(EulerZxy.ToMatrix(angles)
            * Matrix4x4.CreateFromAxisAngle(TranslateGizmo.Direction(axis), degrees * (MathF.PI / 180f)));

    /// <summary>Where along the ray it crosses the ring's plane, or null when that is behind the eye
    /// or the ray runs too nearly along the plane.</summary>
    private static float? Crossing(Vector3 rayOrigin, Vector3 rayDirection, Vector3 origin, GizmoAxis axis)
    {
        Vector3 normal = TranslateGizmo.Direction(axis);
        float slant = Vector3.Dot(rayDirection, normal);
        if (MathF.Abs(slant) < MinSlant)
        {
            return null;
        }

        float t = Vector3.Dot(origin - rayOrigin, normal) / slant;
        return t >= 0f ? t : null;
    }

    /// <summary>The angle of <paramref name="offset"/> round the axis, counter-clockwise looking down
    /// it, measured in the same basis the ring is drawn in.</summary>
    private static float AngleOnRing(GizmoAxis axis, Vector3 offset)
    {
        Matrix4x4 basis = TranslateGizmo.Orientation(axis);
        var across = new Vector3(basis.M11, basis.M12, basis.M13);
        var up = new Vector3(basis.M21, basis.M22, basis.M23);
        return MathF.Atan2(Vector3.Dot(offset, up), Vector3.Dot(offset, across)) * (180f / MathF.PI);
    }
}
