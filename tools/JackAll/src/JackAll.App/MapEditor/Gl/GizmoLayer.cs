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
    /// <summary>Where the shaft starts and stops, and where the head widens, along a unit arm.</summary>
    private const float ShaftStart = 0.08f;
    private const float ShaftEnd = 0.76f;
    private const float ShaftRadius = 0.018f;
    private const float HeadRadius = 0.07f;
    private const int Sides = 10;

    /// <summary>How many segments make a ring's circle.</summary>
    private const int RingSegments = 64;

    private const float PlaneAlpha = 0.35f;
    private const float ActivePlaneAlpha = 0.75f;

    private static readonly Vector3 Highlight = new(1f, 0.92f, 0.4f);

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
    private GizmoLayer(List<Vector3> handle, List<Vector3> plane)
    {
        _handleVertices = handle.Count;
        _planeVertices = plane.Count;
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

    /// <summary>The move gizmo: a shaft, a cone and the cap under it, along +Z of unit length, and
    /// the square in the XY plane that each pair of arms frames.</summary>
    public static GizmoLayer Arrows()
    {
        var vertices = new List<Vector3>();
        for (int side = 0; side < Sides; side++)
        {
            (float cosA, float sinA) = Turn(side, Sides);
            (float cosB, float sinB) = Turn(side + 1, Sides);

            Vector3 shaftA = new(cosA * ShaftRadius, sinA * ShaftRadius, 0f);
            Vector3 shaftB = new(cosB * ShaftRadius, sinB * ShaftRadius, 0f);
            Add(vertices, shaftA + Along(ShaftStart), shaftB + Along(ShaftStart), shaftB + Along(ShaftEnd));
            Add(vertices, shaftA + Along(ShaftStart), shaftB + Along(ShaftEnd), shaftA + Along(ShaftEnd));

            Vector3 headA = new(cosA * HeadRadius, sinA * HeadRadius, 0f);
            Vector3 headB = new(cosB * HeadRadius, sinB * HeadRadius, 0f);
            Add(vertices, headA + Along(ShaftEnd), headB + Along(ShaftEnd), Along(1f));
            Add(vertices, headB + Along(ShaftEnd), headA + Along(ShaftEnd), Along(ShaftEnd));
        }

        const float a = TranslateGizmo.PlaneStart, b = TranslateGizmo.PlaneEnd;
        var plane = new List<Vector3>();
        Add(plane, new Vector3(a, a, 0f), new Vector3(b, a, 0f), new Vector3(b, b, 0f));
        Add(plane, new Vector3(a, a, 0f), new Vector3(b, b, 0f), new Vector3(a, b, 0f));
        return new GizmoLayer(vertices, plane);
    }

    /// <summary>The rotate gizmo: a thin tube round the unit circle in the plane square to +Z, in the
    /// same basis <see cref="RotateGizmo"/> measures its angles in.</summary>
    public static GizmoLayer Rings()
    {
        var vertices = new List<Vector3>();
        for (int segment = 0; segment < RingSegments; segment++)
        {
            (float cosA, float sinA) = Turn(segment, RingSegments);
            (float cosB, float sinB) = Turn(segment + 1, RingSegments);
            for (int side = 0; side < Sides; side++)
            {
                (float cosS, float sinS) = Turn(side, Sides);
                (float cosT, float sinT) = Turn(side + 1, Sides);
                Vector3 a0 = OnTube(cosA, sinA, cosS, sinS), a1 = OnTube(cosA, sinA, cosT, sinT);
                Vector3 b0 = OnTube(cosB, sinB, cosS, sinS), b1 = OnTube(cosB, sinB, cosT, sinT);
                Add(vertices, a0, b0, b1);
                Add(vertices, a0, b1, a1);
            }
        }
        return new GizmoLayer(vertices, []);

        static Vector3 OnTube(float cosRing, float sinRing, float cosTube, float sinTube)
        {
            float reach = 1f + (cosTube * ShaftRadius);
            return new Vector3(cosRing * reach, sinRing * reach, sinTube * ShaftRadius);
        }
    }

    private static (float Cos, float Sin) Turn(int step, int steps)
    {
        float angle = MathHelper.TwoPi * step / steps;
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
            GL.Uniform4(_uTint, new Vector4(axis == active ? Highlight : Tint(axis), 1f));
            GL.DrawArrays(PrimitiveType.Triangles, 0, _handleVertices);
        }

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        foreach (GizmoAxis plane in _planeVertices > 0 ? TranslateGizmo.Planes : [])
        {
            Matrix4 model = GlMatrix.From(TranslateGizmo.PlaneOrientation(plane)) * place;
            (GizmoAxis u, GizmoAxis v) = TranslateGizmo.ArmsOf(plane);
            Vector3 tint = plane == active ? Highlight : (Tint(u) + Tint(v)) * 0.5f;
            GL.UniformMatrix4(_uModel, false, ref model);
            GL.Uniform4(_uTint, new Vector4(tint, plane == active ? ActivePlaneAlpha : PlaneAlpha));
            GL.DrawArrays(PrimitiveType.Triangles, _handleVertices, _planeVertices);
        }
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
