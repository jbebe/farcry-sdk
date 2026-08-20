using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>The bridge between the world model's matrices and the ones the layers upload.</summary>
public static class GlMatrix
{
    /// <summary>
    /// A <c>System.Numerics</c> matrix as OpenTK's, row for row. Both are row-vector and row-major,
    /// and the uniform upload leaves its transpose flag off - which is what turns either of them into
    /// the column-vector form the shaders multiply in.
    /// </summary>
    public static Matrix4 From(System.Numerics.Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}
