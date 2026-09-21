using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>
/// The engine's <c>hidAngles</c> convention: ZXY Euler in degrees, yaw applied last. The one
/// definition - the map's vertex shader mirrors <see cref="ToMatrix"/> and nothing else re-derives it.
/// </summary>
public static class EulerZxy
{
    public static Matrix4x4 ToMatrix(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180f);
        return Matrix4x4.CreateRotationY(radians.Y)
            * Matrix4x4.CreateRotationX(radians.X)
            * Matrix4x4.CreateRotationZ(radians.Z);
    }

    /// <summary>The angles <see cref="ToMatrix"/> turns into <paramref name="rotation"/>. At the
    /// gimbal (X at ±90°) Y and Z share one freedom, and Y is taken as 0.</summary>
    public static Vector3 FromMatrix(Matrix4x4 rotation)
    {
        float x = MathF.Asin(Math.Clamp(rotation.M23, -1f, 1f));
        float y, z;
        if (MathF.Abs(MathF.Cos(x)) > 1e-4f)
        {
            y = MathF.Atan2(-rotation.M13, rotation.M33);
            z = MathF.Atan2(-rotation.M21, rotation.M22);
        }
        else
        {
            y = 0f;
            z = MathF.Atan2(rotation.M12, rotation.M11);
        }
        return new Vector3(x, y, z) * (180f / MathF.PI);
    }
}
