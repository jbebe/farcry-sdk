using System.Numerics;
using JackAll.Tools.World;

namespace JackAll.Tests;

/// <summary>
/// The move gizmo's geometry: which arm a click lands on, and where the entity ends up as the cursor
/// drags it. All of it is solved against rays, so none of it needs a viewport.
/// </summary>
public class TranslateGizmoTests
{
    private static readonly Vector3 Origin = new(10f, 20f, 5f);
    private const float Scale = 4f;

    /// <summary>
    /// A ray from off to one side, aimed at a world point. The eye sits level with the gizmo and
    /// square-ish to all three arms: from above, a ray reaching for one arm passes through where the
    /// others bunch near the origin, and the nearer one rightly takes the grab.
    /// </summary>
    private static (Vector3 Origin, Vector3 Direction) Aim(Vector3 at, Vector3? from = null)
    {
        Vector3 eye = from ?? Origin + new Vector3(-12f, -28f, 0f);
        return (eye, Vector3.Normalize(at - eye));
    }

    private static GizmoGrab Grab(Vector3 at)
    {
        (Vector3 eye, Vector3 direction) = Aim(at);
        GizmoGrab? grabbed = TranslateGizmo.Grab(eye, direction, Origin, Scale);
        Assert.NotNull(grabbed);
        return grabbed.Value;
    }

    [Theory]
    [InlineData(1, 0, 0, GizmoAxis.X)]
    [InlineData(0, 1, 0, GizmoAxis.Y)]
    [InlineData(0, 0, 1, GizmoAxis.Z)]
    public void A_click_on_an_arm_grabs_that_arm(float x, float y, float z, GizmoAxis expected)
    {
        var arm = new Vector3(x, y, z);
        GizmoGrab grabbed = Grab(Origin + (arm * (Scale * 0.5f)));

        Assert.Equal(expected, grabbed.Axis);
        Assert.Equal(Scale * 0.5f, grabbed.Along, 2);
        Assert.Equal(Origin, grabbed.Origin);
    }

    /// <summary>The arms are finite: past the cone there is nothing to take hold of.</summary>
    [Fact]
    public void A_click_past_the_tip_grabs_nothing()
    {
        (Vector3 eye, Vector3 direction) = Aim(Origin + new Vector3(Scale * 1.5f, 0f, 0f));

        Assert.Null(TranslateGizmo.Grab(eye, direction, Origin, Scale));
    }

    /// <summary>Wide of every arm is a click that should fall through to selecting what is behind.</summary>
    [Fact]
    public void A_click_away_from_every_arm_grabs_nothing()
    {
        (Vector3 eye, Vector3 direction) = Aim(Origin + new Vector3(2f, 2f, 2f));

        Assert.Null(TranslateGizmo.Grab(eye, direction, Origin, Scale));
    }

    /// <summary>The grab tolerance is a slice of the gizmo's size, so it holds at any distance.</summary>
    [Fact]
    public void The_grab_reaches_a_little_way_off_the_arm_and_no_further()
    {
        Vector3 near = Origin + new Vector3(2f, 0f, Scale * (TranslateGizmo.GrabRadius * 0.5f));
        Vector3 far = Origin + new Vector3(2f, 0f, Scale * (TranslateGizmo.GrabRadius * 2f));

        (Vector3 nearEye, Vector3 nearDirection) = Aim(near);
        (Vector3 farEye, Vector3 farDirection) = Aim(far);

        Assert.NotNull(TranslateGizmo.Grab(nearEye, nearDirection, Origin, Scale));
        Assert.Null(TranslateGizmo.Grab(farEye, farDirection, Origin, Scale));
    }

    /// <summary>
    /// The entity must not jump the moment it is grabbed - following the very ray that grabbed it
    /// has to leave it exactly where it stood, or every drag starts by snapping the entity to the
    /// cursor.
    /// </summary>
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void Grabbing_alone_does_not_move_the_entity(float x, float y, float z)
    {
        var arm = new Vector3(x, y, z);
        Vector3 at = Origin + (arm * (Scale * 0.6f));
        GizmoGrab grabbed = Grab(at);

        (Vector3 eye, Vector3 direction) = Aim(at);
        Vector3? followed = TranslateGizmo.Follow(grabbed, eye, direction);

        Assert.NotNull(followed);
        Assert.Equal(Origin.X, followed.Value.X, 3);
        Assert.Equal(Origin.Y, followed.Value.Y, 3);
        Assert.Equal(Origin.Z, followed.Value.Z, 3);
    }

    /// <summary>A drag moves by exactly what the cursor slid along the arm, and only along it.</summary>
    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void A_drag_moves_along_its_own_arm_only(float x, float y, float z)
    {
        var arm = new Vector3(x, y, z);
        GizmoGrab grabbed = Grab(Origin + (arm * 2f));

        // Aim 1.5 m further along the arm's line; the solve should carry the entity by exactly that.
        (Vector3 eye, Vector3 direction) = Aim(Origin + (arm * 3.5f));
        Vector3 moved = TranslateGizmo.Follow(grabbed, eye, direction)!.Value;

        Vector3 expected = Origin + (arm * 1.5f);
        Assert.Equal(expected.X, moved.X, 3);
        Assert.Equal(expected.Y, moved.Y, 3);
        Assert.Equal(expected.Z, moved.Z, 3);
    }

    /// <summary>
    /// The arm stays anchored where the entity was grabbed. Re-solving against the entity's new
    /// position instead would feed each frame's movement into the next and run the entity away.
    /// </summary>
    [Fact]
    public void A_drag_solves_against_where_the_entity_was_grabbed()
    {
        GizmoGrab grabbed = Grab(Origin + new Vector3(2f, 0f, 0f));
        (Vector3 eye, Vector3 direction) = Aim(Origin + new Vector3(4f, 0f, 0f));

        Vector3 once = TranslateGizmo.Follow(grabbed, eye, direction)!.Value;
        Vector3 twice = TranslateGizmo.Follow(grabbed, eye, direction)!.Value;

        Assert.Equal(once, twice);
        Assert.Equal(Origin.X + 2f, once.X, 3);
    }

    /// <summary>Sighting down an arm leaves it edge-on, where a pixel of cursor movement would throw
    /// the entity a long way. It holds still instead.</summary>
    [Fact]
    public void An_arm_seen_end_on_refuses_to_solve()
    {
        var grabbed = new GizmoGrab(GizmoAxis.X, Origin, 2f);
        Vector3 eye = Origin - new Vector3(40f, 0f, 0f);

        Assert.Null(TranslateGizmo.Follow(grabbed, eye, Vector3.UnitX));
    }

    /// <summary>
    /// The arrow model is built along +Z, so each arm's orientation has to land its tip on that
    /// arm's own axis - the difference between a gizmo and three arrows pointing the wrong way.
    /// </summary>
    [Theory]
    [InlineData(GizmoAxis.X, 1, 0, 0)]
    [InlineData(GizmoAxis.Y, 0, 1, 0)]
    [InlineData(GizmoAxis.Z, 0, 0, 1)]
    public void An_arms_orientation_turns_the_models_tip_onto_that_axis(
        GizmoAxis axis, float x, float y, float z)
    {
        Matrix4x4 orientation = TranslateGizmo.Orientation(axis);
        Vector3 tip = Vector3.Transform(Vector3.UnitZ, orientation);

        Assert.Equal(x, tip.X, 4);
        Assert.Equal(y, tip.Y, 4);
        Assert.Equal(z, tip.Z, 4);

        // A rotation and nothing else, or the arrow arrives stretched or mirrored.
        Assert.Equal(1f, orientation.GetDeterminant(), 4);
    }

    /// <summary>The gizmo holds its size on screen, so its world size tracks how far off it is.</summary>
    [Fact]
    public void The_gizmo_grows_with_its_distance_from_the_camera()
    {
        float near = TranslateGizmo.Scale(
            new Vector3(0f, 10f, 0f), Vector3.Zero, MathF.PI / 3f, 800f, 100f);
        float far = TranslateGizmo.Scale(
            new Vector3(0f, 20f, 0f), Vector3.Zero, MathF.PI / 3f, 800f, 100f);

        Assert.True(near > 0f);
        Assert.Equal(near * 2f, far, 4);
    }
}
