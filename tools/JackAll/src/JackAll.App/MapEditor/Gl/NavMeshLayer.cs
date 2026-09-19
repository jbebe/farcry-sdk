using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Draws a map's navmesh: its triangles tinted by slope, their edges by kind, and optionally a line
/// between the centres of every two neighbouring triangles.
/// </summary>
public sealed class NavMeshLayer : IDisposable
{
    private static readonly Vector4 InteriorTint = new(0.9f, 0.9f, 0.9f, 0.35f);
    private static readonly Vector4 BoundaryTint = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 SeamTint = new(0.95f, 0.35f, 0.95f, 1f);
    private static readonly Vector4 LinkTint = new(1f, 0.85f, 0.3f, 0.6f);

    /// <summary>Share of the camera distance each pass is pulled toward the camera, lines further
    /// than the surface, so both win against the ground they lie on.</summary>
    private const float SurfaceLift = 0.003f;
    private const float LineLift = 0.005f;

    private const string VertexSource =
        """
        #version 330 core
        layout(location = 0) in vec3 position;
        uniform mat4 viewProjection;
        uniform vec3 origin;
        uniform float lift;
        out vec3 local;
        void main()
        {
            local = position - origin;
            gl_Position = viewProjection * vec4(position - local * lift, 1.0);
        }
        """;

    private readonly Pass _fill;
    private readonly Pass _line;
    private readonly int _uTint;

    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _ebo;

    /// <summary>Where the centroids start in the vertex buffer, which link indices are relative to.</summary>
    private readonly int _centroidBase;

    /// <summary>Index counts of the triangles, the three edge kinds and the links, stored in that
    /// order in the index buffer.</summary>
    private readonly int[] _counts;

    private sealed record Pass(ShaderProgram Program, int ViewProjection, int Origin, int Lift);

    public NavMeshLayer(NavMesh mesh)
    {
        _fill = Compile(
            """
            #version 330 core
            in vec3 local;
            out vec4 fragment;
            void main()
            {
                vec3 normal = normalize(cross(dFdx(local), dFdy(local)));
                float level = abs(normal.z);
                fragment = vec4(1.0 - level, level, 0.25, 0.35);
            }
            """);
        _line = Compile(
            """
            #version 330 core
            uniform vec4 tint;
            out vec4 fragment;
            void main() { fragment = tint; }
            """);
        _uTint = _line.Program.UniformLocation("tint");

        _centroidBase = mesh.Vertices.Length;
        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        _vbo = GL.GenBuffer();
        Upload(BufferTarget.ArrayBuffer, _vbo, (mesh.Vertices.Length + mesh.Centroids.Length) * 3 * sizeof(float),
            [mesh.Vertices, mesh.Centroids], 3 * sizeof(float));
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

        int[][] ranges = [mesh.Triangles, mesh.InteriorEdges, mesh.BoundaryEdges, mesh.SeamEdges, mesh.Links];
        _counts = [.. ranges.Select(r => r.Length)];
        _ebo = GL.GenBuffer();
        Upload(BufferTarget.ElementArrayBuffer, _ebo, _counts.Sum() * sizeof(int), ranges, sizeof(int));
        GL.BindVertexArray(0);
    }

    private static Pass Compile(string fragmentSource)
    {
        var program = new ShaderProgram(VertexSource, fragmentSource);
        return new Pass(program, program.UniformLocation("viewProjection"), program.UniformLocation("origin"),
            program.UniformLocation("lift"));
    }

    /// <summary>Fills one buffer with the arrays back to back, without joining them first.</summary>
    private static void Upload<T>(BufferTarget target, int buffer, int bytes, T[][] parts, int elementSize)
        where T : struct
    {
        GL.BindBuffer(target, buffer);
        GL.BufferData(target, bytes, IntPtr.Zero, BufferUsageHint.StaticDraw);
        int offset = 0;
        foreach (T[] part in parts)
        {
            GL.BufferSubData(target, offset, part.Length * elementSize, part);
            offset += part.Length * elementSize;
        }
    }

    public void Draw(Matrix4 viewProjection, Vector3 cameraPosition, bool surface, bool edges, bool links)
    {
        if (_counts[0] == 0)
        {
            return;
        }

        GL.BindVertexArray(_vao);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        if (surface)
        {
            Use(_fill, viewProjection, cameraPosition, SurfaceLift);
            GL.DepthMask(false);
            GL.DrawElements(PrimitiveType.Triangles, _counts[0], DrawElementsType.UnsignedInt, 0);
            GL.DepthMask(true);
        }

        Use(_line, viewProjection, cameraPosition, LineLift);
        if (edges)
        {
            DrawLines(InteriorTint, 1f, 1, 0);
            DrawLines(BoundaryTint, 2f, 2, 0);
            DrawLines(SeamTint, 2f, 3, 0);
        }
        if (links)
        {
            DrawLines(LinkTint, 1f, 4, _centroidBase);
        }

        GL.Disable(EnableCap.Blend);
        GL.BindVertexArray(0);
    }

    private static void Use(Pass pass, Matrix4 viewProjection, Vector3 cameraPosition, float lift)
    {
        pass.Program.Use();
        GL.UniformMatrix4(pass.ViewProjection, false, ref viewProjection);
        GL.Uniform3(pass.Origin, cameraPosition);
        GL.Uniform1(pass.Lift, lift);
    }

    /// <summary>Draws one range of the index buffer as lines, its indices offset by
    /// <paramref name="baseVertex"/>.</summary>
    private void DrawLines(Vector4 tint, float width, int range, int baseVertex)
    {
        if (_counts[range] == 0)
        {
            return;
        }

        GL.Uniform4(_uTint, tint);
        GL.LineWidth(width);
        GL.DrawElementsBaseVertex(PrimitiveType.Lines, _counts[range], DrawElementsType.UnsignedInt,
            _counts.Take(range).Sum() * sizeof(int), baseVertex);
    }

    public void Dispose()
    {
        _fill.Program.Dispose();
        _line.Program.Dispose();
        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_ebo);
        GL.DeleteVertexArray(_vao);
    }
}
