using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// Draws every positioned entity as an instanced billboard quad from the session's pre-baked
/// <c>[x, y, z, r, g, b]</c> stream, plus a highlight quad over the current selection. The same
/// program serves both viewport modes: 2D passes ground-plane axes and flattens Z; 3D passes the
/// camera's right/up so markers face it.
/// </summary>
public sealed class EntityMarkerLayer : IDisposable
{
    public const int Stride = 6;

    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _highlightVao;
    private readonly int _instanceBuffer;
    private int _instanceCount;
    private readonly int _uProjection;
    private readonly int _uMarkerUnits;
    private readonly int _uRight;
    private readonly int _uUp;
    private readonly int _uFlattenZ;

    public EntityMarkerLayer(float[] instances, int instanceCount)
        : this(instanceCount)
        => SetInstances(instances, instanceCount);

    /// <summary>A layer whose stream is refilled via <see cref="SetInstances"/> - the program, VAO
    /// and capacity-sized buffer survive every refill, so a rebuild is one BufferSubData.</summary>
    public EntityMarkerLayer(int capacity)
    {
        _instanceBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, Math.Max(1, capacity) * Stride * sizeof(float),
            IntPtr.Zero, BufferUsageHint.DynamicDraw);

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceBuffer);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Stride * sizeof(float), 0);
        GL.VertexAttribDivisor(0, 1);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, Stride * sizeof(float), 3 * sizeof(float));
        GL.VertexAttribDivisor(1, 1);

        // The highlight draw uses this VAO with no arrays enabled, so both attributes fall back to
        // the constant generic values set just before the draw - no state juggling on the main VAO.
        _highlightVao = GL.GenVertexArray();

        _program = new ShaderProgram(
            $$"""
            #version 330 core
            layout(location = 0) in vec3 center;
            layout(location = 1) in vec3 color;
            uniform mat4 projection;
            uniform float markerUnits;
            uniform vec3 right;
            uniform vec3 up;
            uniform float flattenZ;
            {{ShaderProgram.QuadCornerGlsl}}
            out vec3 markerColor;
            void main()
            {
                vec2 corner = quadCorner() - 0.5;
                vec3 pos = vec3(center.xy, center.z * flattenZ)
                         + (right * corner.x + up * corner.y) * markerUnits;
                gl_Position = projection * vec4(pos, 1.0);
                markerColor = color;
            }
            """,
            """
            #version 330 core
            in vec3 markerColor;
            out vec4 fragment;
            void main() { fragment = vec4(markerColor, 0.9); }
            """);
        _uProjection = _program.UniformLocation("projection");
        _uMarkerUnits = _program.UniformLocation("markerUnits");
        _uRight = _program.UniformLocation("right");
        _uUp = _program.UniformLocation("up");
        _uFlattenZ = _program.UniformLocation("flattenZ");
    }

    /// <summary>Rewrites the live instances in place; <paramref name="count"/> must fit the
    /// capacity the layer was constructed with.</summary>
    public void SetInstances(float[] instances, int count)
    {
        _instanceCount = count;
        if (count == 0)
        {
            return;
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceBuffer);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, count * Stride * sizeof(float), instances);
    }

    public void Draw(Matrix4 projection, float markerUnits, Vector3 right, Vector3 up, bool flattenZ,
        WorldEntity? selected)
    {
        _program.Use();
        GL.UniformMatrix4(_uProjection, false, ref projection);
        GL.Uniform1(_uMarkerUnits, markerUnits);
        GL.Uniform3(_uRight, right);
        GL.Uniform3(_uUp, up);
        GL.Uniform1(_uFlattenZ, flattenZ ? 0f : 1f);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.BindVertexArray(_vao);
        GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, _instanceCount);

        if (selected?.Position is { } pos)
        {
            GL.Uniform1(_uMarkerUnits, markerUnits * 2.2f);
            GL.BindVertexArray(_highlightVao);
            GL.VertexAttrib3(0, pos.X, pos.Y, pos.Z);
            GL.VertexAttrib3(1, 1f, 1f, 0.2f);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }
        GL.Disable(EnableCap.Blend);
    }

    /// <summary>Rewrites one instance's position in place - an entity drag touches 12 bytes, not the
    /// whole 90k-instance stream.</summary>
    public void UpdatePosition(int index, float x, float y, float z)
    {
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceBuffer);
        GL.BufferSubData(BufferTarget.ArrayBuffer, (IntPtr)(index * Stride * sizeof(float)),
            3 * sizeof(float), new[] { x, y, z });
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteVertexArray(_vao);
        GL.DeleteVertexArray(_highlightVao);
        GL.DeleteBuffer(_instanceBuffer);
    }
}
