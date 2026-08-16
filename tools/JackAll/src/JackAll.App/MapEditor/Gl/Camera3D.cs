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

    public Vector3 Forward => new(
        MathF.Sin(Yaw) * MathF.Cos(Pitch),
        MathF.Cos(Yaw) * MathF.Cos(Pitch),
        MathF.Sin(Pitch));

    public Vector3 Right => new(MathF.Cos(Yaw), -MathF.Sin(Yaw), 0);

    public Matrix4 View() => Matrix4.LookAt(
        new Vector3(Position.X, Position.Y, Position.Z),
        new Vector3(Position.X, Position.Y, Position.Z) + Forward,
        Vector3.UnitZ);

    public Matrix4 Projection(float aspect) =>
        Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(60f), MathF.Max(aspect, 0.1f), 0.5f, 8000f);

    public void Look(float dxPixels, float dyPixels)
    {
        Yaw += dxPixels * 0.004f;
        Pitch = Math.Clamp(Pitch - dyPixels * 0.004f, -1.55f, 1.55f);
    }

    /// <summary>WASD-style move in the ground plane plus vertical, scaled by dt and MoveSpeed.</summary>
    public void Move(float forward, float strafe, float lift, float dt)
    {
        Vector3 flatForward = new(MathF.Sin(Yaw), MathF.Cos(Yaw), 0);
        Position += (flatForward * forward + Right * strafe + Vector3.UnitZ * lift) * MoveSpeed * dt;
    }

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
