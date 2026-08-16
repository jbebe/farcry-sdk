using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Draws the world's authored polylines - zone outlines, paths and sound lines - as line strips.
/// Sound lines are tinted apart from the rest so the two kinds are distinguishable.
/// </summary>
public sealed class ShapeLayer : IDisposable
{
    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _vertexCount;
    private readonly int _uViewProjection;

    public int ShapeCount { get; }

    public ShapeLayer(IReadOnlyList<WorldShape> shapes)
    {
        ShapeCount = shapes.Count;

        // Each polyline becomes its own set of segments, so one buffer covers them all without
        // needing a draw call per shape.
        var vertices = new List<float>(shapes.Sum(s => s.Points.Count) * 12);
        foreach (WorldShape shape in shapes)
        {
            (float r, float g, float b) = TintFor(shape.Kind);
            for (int i = 0; i + 1 < shape.Points.Count; i++)
            {
                Append(vertices, shape.Points[i], r, g, b);
                Append(vertices, shape.Points[i + 1], r, g, b);
            }
        }
        _vertexCount = vertices.Count / 6;

        _program = new ShaderProgram(
            """
            #version 330 core
            layout(location = 0) in vec3 position;
            layout(location = 1) in vec3 tint;
            uniform mat4 viewProjection;
            out vec3 lineTint;
            void main()
            {
                lineTint = tint;
                gl_Position = viewProjection * vec4(position, 1.0);
            }
            """,
            """
            #version 330 core
            in vec3 lineTint;
            out vec4 fragment;
            void main() { fragment = vec4(lineTint, 0.9); }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        _vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(),
            BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
    }

    private static (float R, float G, float B) TintFor(string kind) => kind switch
    {
        "road" => (0.85f, 0.65f, 0.30f),
        "river" => (0.30f, 0.55f, 0.95f),
        "path" => (0.70f, 0.45f, 0.85f),
        "sound" => (0.95f, 0.75f, 0.25f),
        _ => (0.35f, 0.85f, 0.95f),
    };

    private static void Append(List<float> into, System.Numerics.Vector3 point, float r, float g, float b)
    {
        into.Add(point.X);
        into.Add(point.Y);
        into.Add(point.Z);
        into.Add(r);
        into.Add(g);
        into.Add(b);
    }

    public void Draw(Matrix4 viewProjection)
    {
        if (_vertexCount == 0)
        {
            return;
        }

        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        GL.BindVertexArray(_vao);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.LineWidth(2f);
        GL.DrawArrays(PrimitiveType.Lines, 0, _vertexCount);
        GL.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
    }
}
