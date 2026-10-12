using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Draws a gizmo's handles: the move gizmo's arrows and plane squares, or the rotate gizmo's rings.
/// One handle built around +Z lives in the buffer and each axis is a rotation of it, so the whole
/// gizmo is a few draws of the same geometry.
/// </summary>
public sealed class GizmoLayer : IDisposable
{
    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _handleVertices;
    private readonly int _planeVertices;
    private readonly int _uViewProjection;
    private readonly int _uModel;
    private readonly int _uTint;

    /// <summary><paramref name="plane"/> is the square drawn between two arms, empty for a gizmo
    /// without one; it follows the handle in the same buffer.</summary>
    private GizmoLayer(System.Numerics.Vector3[] handle, System.Numerics.Vector3[] plane)
    {
        _handleVertices = handle.Length;
        _planeVertices = plane.Length;
        float[] floats = [.. handle.Concat(plane).SelectMany(v => new[] { v.X, v.Y, v.Z })];

        _program = new ShaderProgram(
            """
            #version 330 core
            layout(location = 0) in vec3 corner;
            uniform mat4 viewProjection;
            uniform mat4 model;
            void main() { gl_Position = viewProjection * model * vec4(corner, 1.0); }
            """,
            """
            #version 330 core
            uniform vec4 tint;
            out vec4 fragment;
            void main() { fragment = tint; }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uModel = _program.UniformLocation("model");
        _uTint = _program.UniformLocation("tint");

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        _vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, floats.Length * sizeof(float), floats,
            BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.BindVertexArray(0);
    }

    /// <summary>The move gizmo: its arm, and the square each pair of arms frames.</summary>
    public static GizmoLayer Arrows() => new(GizmoGeometry.Arrow(), GizmoGeometry.Square());

    /// <summary>The rotate gizmo: its ring.</summary>
    public static GizmoLayer Rings() => new(GizmoGeometry.Ring(), []);

    /// <summary>
    /// The handles at <paramref name="origin"/>, sized so the gizmo holds its screen size, with
    /// <paramref name="active"/> lit up.
    /// </summary>
    /// <remarks>
    /// Depth testing and culling both stay off while this draws: a handle you cannot see because the
    /// entity's own model swallows it is a handle you cannot grab, and the geometry is not wound for
    /// culling.
    /// </remarks>
    public void Draw(Matrix4 viewProjection, System.Numerics.Vector3 origin, float scale, GizmoAxis active)
    {
        using var state = new GlState();
        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        GL.BindVertexArray(_vao);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        Matrix4 place = Matrix4.CreateScale(scale) * Matrix4.CreateTranslation(origin.X, origin.Y, origin.Z);

        foreach (GizmoAxis axis in TranslateGizmo.Axes)
        {
            Matrix4 model = GlMatrix.From(TranslateGizmo.Orientation(axis)) * place;
            GL.UniformMatrix4(_uModel, false, ref model);
            GL.Uniform4(_uTint, Tint(axis, active, 1f, 1f));
            GL.DrawArrays(PrimitiveType.Triangles, 0, _handleVertices);
        }

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        foreach (GizmoAxis plane in _planeVertices > 0 ? TranslateGizmo.Planes : [])
        {
            Matrix4 model = GlMatrix.From(TranslateGizmo.PlaneOrientation(plane)) * place;
            GL.UniformMatrix4(_uModel, false, ref model);
            GL.Uniform4(_uTint, Tint(plane, active, GizmoGeometry.PlaneAlpha, GizmoGeometry.HeldPlaneAlpha));
            GL.DrawArrays(PrimitiveType.Triangles, _handleVertices, _planeVertices);
        }
        GL.BindVertexArray(0);
    }

    private static Vector4 Tint(GizmoAxis axis, GizmoAxis active, float alpha, float heldAlpha)
    {
        System.Numerics.Vector3 c = axis == active ? GizmoGeometry.Highlight : GizmoGeometry.Tint(axis);
        return new Vector4(c.X, c.Y, c.Z, axis == active ? heldAlpha : alpha);
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
    }
}
