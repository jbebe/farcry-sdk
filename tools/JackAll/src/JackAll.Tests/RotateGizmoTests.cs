using System.Numerics;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// The rotate gizmo's geometry: which ring a click lands on, how far round it the cursor drags, and
/// what that turn does to an entity's angles. Solved against rays, so none of it needs a viewport.
/// </summary>
public class RotateGizmoTests
{
    private static readonly Vector3 Origin = new(10f, 20f, 5f);
    private const float Scale = 4f;

    /// <summary>An eye off to one side and above, so no ring is seen edge-on.</summary>
    private static readonly Vector3 Eye = Origin + new Vector3(-12f, -28f, 20f);

    private static (Vector3 Origin, Vector3 Direction) Aim(Vector3 at, Vector3? from = null)
    {
        Vector3 eye = from ?? Eye;
        return (eye, Vector3.Normalize(at - eye));
    }

    /// <summary>The point <paramref name="degrees"/> round a ring, in the basis the ring is drawn in.</summary>
    private static Vector3 OnRing(GizmoAxis axis, float degrees, float radius = Scale)
    {
        Matrix4x4 basis = TranslateGizmo.Orientation(axis);
        var across = new Vector3(basis.M11, basis.M12, basis.M13);
        var up = new Vector3(basis.M21, basis.M22, basis.M23);
        float radians = degrees * (MathF.PI / 180f);
        return Origin + (radius * ((MathF.Cos(radians) * across) + (MathF.Sin(radians) * up)));
    }

    private static RotateGrab Grab(Vector3 at)
    {
        (Vector3 eye, Vector3 direction) = Aim(at);
        RotateGrab? grabbed = RotateGizmo.Grab(eye, direction, Origin, Scale);
        Assert.NotNull(grabbed);
        return grabbed.Value;
    }

    [Theory]
    [InlineData(GizmoAxis.X)]
    [InlineData(GizmoAxis.Y)]
    [InlineData(GizmoAxis.Z)]
    public void A_click_on_a_ring_grabs_that_ring(GizmoAxis axis)
    {
        RotateGrab grabbed = Grab(OnRing(axis, 150f));

        Assert.Equal(axis, grabbed.Axis);
        Assert.Equal(150f, grabbed.StartAngle, 2);
        Assert.Equal(Origin, grabbed.Origin);
    }

    /// <summary>Inside the ring and well outside it is a click meant for whatever is behind.</summary>
    [Theory]
    [InlineData(0.4f)]
    [InlineData(1.6f)]
    public void A_click_off_the_ring_grabs_nothing(float fraction)
    {
        (Vector3 eye, Vector3 direction) = Aim(OnRing(GizmoAxis.Z, 200f, Scale * fraction));

        Assert.Null(RotateGizmo.Grab(eye, direction, Origin, Scale));
    }

    /// <summary>The turn must not jump the moment the ring is grabbed.</summary>
    [Theory]
    [InlineData(GizmoAxis.X)]
    [InlineData(GizmoAxis.Y)]
    [InlineData(GizmoAxis.Z)]
    public void Grabbing_alone_does_not_turn_the_entity(GizmoAxis axis)
    {
        Vector3 at = OnRing(axis, 60f);
        RotateGrab grabbed = Grab(at);

        (Vector3 eye, Vector3 direction) = Aim(at);
        Assert.Equal(0f, RotateGizmo.Follow(grabbed, eye, direction)!.Value, 3);
    }

    /// <summary>A drag turns by exactly the angle the cursor swept round the ring, whatever the
    /// cursor's distance from the centre.</summary>
    [Theory]
    [InlineData(GizmoAxis.X)]
    [InlineData(GizmoAxis.Y)]
    [InlineData(GizmoAxis.Z)]
    public void A_drag_turns_by_the_angle_swept(GizmoAxis axis)
    {
        RotateGrab grabbed = Grab(OnRing(axis, 150f));

        (Vector3 eye, Vector3 direction) = Aim(OnRing(axis, 195f, Scale * 2.5f));
        Assert.Equal(45f, RotateGizmo.Follow(grabbed, eye, direction)!.Value, 2);
    }

    /// <summary>Past half a turn the sweep reads as the short way round, not a leap past 180.</summary>
    [Fact]
    public void A_sweep_across_the_seam_takes_the_short_way()
    {
        RotateGrab grabbed = Grab(OnRing(GizmoAxis.Z, 170f));

        (Vector3 eye, Vector3 direction) = Aim(OnRing(GizmoAxis.Z, -170f));
        Assert.Equal(20f, RotateGizmo.Follow(grabbed, eye, direction)!.Value, 2);
    }

    /// <summary>Sighting along a ring's plane, a pixel of cursor movement sweeps it wildly; it holds.</summary>
    [Fact]
    public void A_ring_seen_edge_on_refuses_to_solve()
    {
        var grabbed = new RotateGrab(GizmoAxis.Z, Origin, 0f);
        Vector3 eye = Origin - new Vector3(40f, 0f, 0f);

        Assert.Null(RotateGizmo.Follow(grabbed, eye, Vector3.UnitX));
    }

    /// <summary>Yaw is applied last, so a turn about world Z adds to the yaw whatever the pitch and roll.</summary>
    [Fact]
    public void A_turn_about_world_z_adds_to_the_yaw()
    {
        Vector3 turned = RotateGizmo.Apply(new Vector3(10f, 20f, 30f), GizmoAxis.Z, 15f);

        Assert.Equal(10f, turned.X, 3);
        Assert.Equal(20f, turned.Y, 3);
        Assert.Equal(45f, turned.Z, 3);
    }

    /// <summary>Any turn about any axis lands on the orientation the rotation itself describes.</summary>
    [Theory]
    [InlineData(GizmoAxis.X, 25f)]
    [InlineData(GizmoAxis.Y, -40f)]
    [InlineData(GizmoAxis.Z, 110f)]
    public void A_turn_composes_the_world_rotation_after_the_current_one(GizmoAxis axis, float degrees)
    {
        var angles = new Vector3(12f, -33f, 71f);
        Matrix4x4 expected = EulerZxy.ToMatrix(angles)
            * Matrix4x4.CreateFromAxisAngle(TranslateGizmo.Direction(axis), degrees * (MathF.PI / 180f));

        EulerZxyTests.AssertSameRotation(expected, EulerZxy.ToMatrix(RotateGizmo.Apply(angles, axis, degrees)));
    }
}
