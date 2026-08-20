using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Draws the move gizmo's three arms. One unit arrow along +Z lives in the buffer and each arm is a
/// rotation of it, so the whole gizmo is three draws of the same geometry.
/// </summary>
public sealed class TranslateGizmoLayer : IDisposable
{
    /// <summary>Where the shaft starts and stops, and where the head widens, along a unit arm.</summary>
    private const float ShaftStart = 0.08f;
    private const float ShaftEnd = 0.76f;
    private const float ShaftRadius = 0.018f;
    private const float HeadRadius = 0.07f;
    private const int Sides = 10;

    private static readonly Vector3 Highlight = new(1f, 0.92f, 0.4f);

    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _vertexCount;
    private readonly int _uViewProjection;
    private readonly int _uModel;
    private readonly int _uTint;

    public TranslateGizmoLayer()
    {
        float[] arrow = UnitArrow();
        _vertexCount = arrow.Length / 3;

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
        GL.BufferData(BufferTarget.ArrayBuffer, arrow.Length * sizeof(float), arrow,
            BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        GL.BindVertexArray(0);
    }

    /// <summary>A shaft, a cone and the cap under it, as a triangle list along +Z of unit length.</summary>
    private static float[] UnitArrow()
    {
        var vertices = new List<Vector3>();
        for (int side = 0; side < Sides; side++)
        {
            (float cosA, float sinA) = Turn(side);
            (float cosB, float sinB) = Turn(side + 1);

            Vector3 shaftA = new(cosA * ShaftRadius, sinA * ShaftRadius, 0f);
            Vector3 shaftB = new(cosB * ShaftRadius, sinB * ShaftRadius, 0f);
            Add(vertices, shaftA + Along(ShaftStart), shaftB + Along(ShaftStart), shaftB + Along(ShaftEnd));
            Add(vertices, shaftA + Along(ShaftStart), shaftB + Along(ShaftEnd), shaftA + Along(ShaftEnd));

            Vector3 headA = new(cosA * HeadRadius, sinA * HeadRadius, 0f);
            Vector3 headB = new(cosB * HeadRadius, sinB * HeadRadius, 0f);
            Add(vertices, headA + Along(ShaftEnd), headB + Along(ShaftEnd), Along(1f));
            Add(vertices, headB + Along(ShaftEnd), headA + Along(ShaftEnd), Along(ShaftEnd));
        }

        var floats = new float[vertices.Count * 3];
        for (int i = 0; i < vertices.Count; i++)
        {
            floats[i * 3] = vertices[i].X;
            floats[i * 3 + 1] = vertices[i].Y;
            floats[i * 3 + 2] = vertices[i].Z;
        }

        return floats;
    }

    private static (float Cos, float Sin) Turn(int side)
    {
        float angle = MathHelper.TwoPi * side / Sides;
        return (MathF.Cos(angle), MathF.Sin(angle));
    }

    private static Vector3 Along(float distance) => new(0f, 0f, distance);

    private static void Add(List<Vector3> into, Vector3 a, Vector3 b, Vector3 c)
    {
        into.Add(a);
        into.Add(b);
        into.Add(c);
    }

    /// <summary>
    /// The three arms at <paramref name="origin"/>, sized so the gizmo holds its screen size, with
    /// <paramref name="active"/> lit up.
    /// </summary>
    /// <remarks>
    /// Depth testing and culling both stay off while this draws: an arm you cannot see because the
    /// entity's own model swallows it is an arm you cannot grab, and the arrow is not wound for
    /// culling. Culling is put back the way it was found rather than simply switched on - the rest of
    /// the map draws with it off, and leaving it on turns every mesh in the world inside out.
    /// </remarks>
    public void Draw(Matrix4 viewProjection, System.Numerics.Vector3 origin, float scale, GizmoAxis active)
    {
        bool wasCulling = GL.IsEnabled(EnableCap.CullFace);
        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        GL.BindVertexArray(_vao);
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        foreach (GizmoAxis axis in TranslateGizmo.Axes)
        {
            Matrix4 model = Matrix4.CreateScale(scale)
                * GlMatrix.From(TranslateGizmo.Orientation(axis))
                * Matrix4.CreateTranslation(origin.X, origin.Y, origin.Z);
            GL.UniformMatrix4(_uModel, false, ref model);
            GL.Uniform3(_uTint, axis == active ? Highlight : Tint(axis));
            GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
        }

        if (wasCulling)
        {
            GL.Enable(EnableCap.CullFace);
        }

        GL.Enable(EnableCap.DepthTest);
        GL.BindVertexArray(0);
    }

    private static Vector3 Tint(GizmoAxis axis) => axis switch
    {
        GizmoAxis.X => new Vector3(0.92f, 0.29f, 0.31f),
        GizmoAxis.Y => new Vector3(0.42f, 0.83f, 0.31f),
        _ => new Vector3(0.32f, 0.55f, 0.98f),
    };

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
    }
}
