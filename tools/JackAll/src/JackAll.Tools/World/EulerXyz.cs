using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>
/// The engine's <c>hidAngles</c> convention: Euler degrees applied about X, then Y, then Z, as
/// <c>CEntity::SetAngles</c> builds the matrix and <c>SBindingHierarchyEntry::OverWriteTransform</c>
/// takes it apart. The one definition - the map's vertex shader mirrors <see cref="ToMatrix"/>.
/// </summary>
public static class EulerXyz
{
    public static Matrix4x4 ToMatrix(Vector3 degrees)
    {
        Vector3 radians = degrees * (MathF.PI / 180f);
        return Matrix4x4.CreateRotationX(radians.X)
            * Matrix4x4.CreateRotationY(radians.Y)
            * Matrix4x4.CreateRotationZ(radians.Z);
    }

    /// <summary>The angles <see cref="ToMatrix"/> turns into <paramref name="rotation"/>. At the
    /// gimbal (Y at ±90°) X and Z share one freedom, and X is taken as 0, as the engine takes it.</summary>
    public static Vector3 FromMatrix(Matrix4x4 rotation)
    {
        float y = MathF.Asin(Math.Clamp(-rotation.M13, -1f, 1f));
        float x, z;
        if (MathF.Abs(MathF.Cos(y)) > 1e-4f)
        {
            x = MathF.Atan2(rotation.M23, rotation.M33);
            z = MathF.Atan2(rotation.M12, rotation.M11);
        }
        else
        {
            x = 0f;
            z = MathF.Atan2(-rotation.M21, rotation.M22);
        }
        return new Vector3(x, y, z) * (180f / MathF.PI);
    }
}
