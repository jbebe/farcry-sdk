using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Draws the wireframe box around the selected entity. The cube's twelve edges live in the buffer
/// once and every selection is a model matrix over them, so switching selection uploads nothing.
/// </summary>
public sealed class SelectionBoxLayer : IDisposable
{
    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _uViewProjection;
    private readonly int _uModel;
    private readonly int _uTint;

    public SelectionBoxLayer()
    {
        float[] corners = UnitCubeEdges();

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
            uniform vec3 tint;
            out vec4 fragment;
            void main() { fragment = vec4(tint, 1.0); }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uModel = _program.UniformLocation("model");
        _uTint = _program.UniformLocation("tint");

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        _vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, corners.Length * sizeof(float), corners,
            BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.BindVertexArray(0);
    }

    /// <summary>Twelve edges as line pairs over the corners of a unit cube centred on the origin.</summary>
    private static float[] UnitCubeEdges()
    {
        var corner = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corner[i] = new Vector3(
                (i & 1) == 0 ? -0.5f : 0.5f,
                (i & 2) == 0 ? -0.5f : 0.5f,
                (i & 4) == 0 ? -0.5f : 0.5f);
        }

        int[] edges =
        [
            0, 1, 2, 3, 4, 5, 6, 7,
            0, 2, 1, 3, 4, 6, 5, 7,
            0, 4, 1, 5, 2, 6, 3, 7,
        ];

        var vertices = new float[edges.Length * 3];
        for (int i = 0; i < edges.Length; i++)
        {
            Vector3 v = corner[edges[i]];
            vertices[i * 3] = v.X;
            vertices[i * 3 + 1] = v.Y;
            vertices[i * 3 + 2] = v.Z;
        }

        return vertices;
    }

    /// <summary>Depth testing stays off so the box reads through whatever it encloses - a selection
    /// you cannot see inside a wall is the thing this exists to fix.</summary>
    public void Draw(Matrix4 viewProjection, Matrix4 model, Vector3 tint)
    {
        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        GL.UniformMatrix4(_uModel, false, ref model);
        GL.Uniform3(_uTint, tint);
        GL.BindVertexArray(_vao);
        GL.Disable(EnableCap.DepthTest);
        GL.LineWidth(2f);
        GL.DrawArrays(PrimitiveType.Lines, 0, 24);
        GL.Enable(EnableCap.DepthTest);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
    }
}
