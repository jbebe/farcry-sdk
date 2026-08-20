using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The fly camera for the 3D mode, working directly in world coordinates (X east, Y north, Z up) -
/// the view matrix carries the Z-up convention, so nothing else ever axis-swaps.
/// </summary>
public sealed class Camera3D
{
    public Vector3 Position { get; set; } = new(2560, 2560, 120);

    /// <summary>Radians; 0 looks along +Y (north), positive turns clockwise viewed from above.</summary>
    public float Yaw { get; set; }

    /// <summary>Radians; positive looks up.</summary>
    public float Pitch { get; set; } = -0.4f;

    public float MoveSpeed { get; set; } = 60f;

    /// <summary>Seconds of held input to reach <see cref="MoveSpeed"/>.</summary>
    public const float AccelerationSeconds = 0.9f;

    /// <summary>What a fresh keypress flies at, as a fraction of <see cref="MoveSpeed"/>. Not zero,
    /// so a tap still nudges.</summary>
    public const float StandingStartFactor = 0.03f;

    /// <summary>The fraction of <see cref="MoveSpeed"/> to fly at after holding a movement key for
    /// <paramref name="heldSeconds"/>. Quadratic, so tapping steps a fraction of a metre while a
    /// held key still winds up to full speed.</summary>
    public static float SpeedFactor(float heldSeconds)
    {
        float ramp = Math.Clamp(heldSeconds / AccelerationSeconds, 0f, 1f);
        return StandingStartFactor + (1f - StandingStartFactor) * ramp * ramp;
    }

    public Vector3 Forward => new(
        MathF.Sin(Yaw) * MathF.Cos(Pitch),
        MathF.Cos(Yaw) * MathF.Cos(Pitch),
        MathF.Sin(Pitch));

    public Vector3 Right => new(MathF.Cos(Yaw), -MathF.Sin(Yaw), 0);

    public Matrix4 View() => Matrix4.LookAt(
        new Vector3(Position.X, Position.Y, Position.Z),
        new Vector3(Position.X, Position.Y, Position.Z) + Forward,
        Vector3.UnitZ);

    /// <summary>Shared with anything sizing itself against the screen rather than the world - a
    /// marker glyph's pixel size falls out of this and the viewport height.</summary>
    public static readonly float VerticalFovRadians = MathHelper.DegreesToRadians(60f);

    public Matrix4 Projection(float aspect) =>
        Matrix4.CreatePerspectiveFieldOfView(VerticalFovRadians, MathF.Max(aspect, 0.1f), 0.5f, 8000f);

    public void Look(float dxPixels, float dyPixels)
    {
        Yaw += dxPixels * 0.004f;
        Pitch = Math.Clamp(Pitch - dyPixels * 0.004f, -1.55f, 1.55f);
    }

    /// <summary>The world-space unit direction WASD/QE input flies in. Forward follows the view
    /// rather than the ground plane, so looking up and holding W climbs.</summary>
    public Vector3 MoveDirection(float forward, float strafe, float lift)
    {
        Vector3 move = Forward * forward + Right * strafe + Vector3.UnitZ * lift;
        return move.LengthSquared > 1e-6f ? Vector3.Normalize(move) : Vector3.Zero;
    }

    public void Move(Vector3 direction, float meters) => Position += direction * meters;

    /// <summary>The world-space ray under a viewport pixel, unprojected through the inverse
    /// view-projection.</summary>
    public (Vector3 Origin, Vector3 Direction) Ray(double px, double py, double viewWidth, double viewHeight)
    {
        var ndc = new Vector4(
            (float)(px / viewWidth * 2 - 1),
            (float)(1 - py / viewHeight * 2),
            -1f, 1f);
        Matrix4 inverse = Matrix4.Invert(View() * Projection((float)(viewWidth / Math.Max(viewHeight, 1))));
        Vector4 world = ndc * inverse;
        world /= world.W;
        return (Position, Vector3.Normalize(new Vector3(world.X, world.Y, world.Z) - Position));
    }
}
