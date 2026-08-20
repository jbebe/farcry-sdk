using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>The shape a marker draws. <see cref="Square"/> is the plain filled quad the bulk layers
/// use; the rest are the category glyphs, drawn as outlines so a dense field of them stays readable
/// instead of merging into a wall of colour.</summary>
public enum MarkerGlyph
{
    Square,
    /// <summary>Ring with a core - a burst, for particle and sound emitters.</summary>
    Burst,
    /// <summary>Cone, for an AI reference point.</summary>
    Cone,
    /// <summary>Open-bottomed frame, for a building entrance hint.</summary>
    Doorway,
    /// <summary>Diamond outline, for a logic node.</summary>
    Diamond,
}

/// <summary>
/// How a marker layer draws. A marker is either sized in world metres - which is what the dense
/// bulk layers want, so distant fields thin out naturally - or held at a constant size on screen,
/// which is what an annotation glyph wants so it stays legible without ever growing into the scene.
/// </summary>
public readonly record struct MarkerStyle(
    MarkerGlyph Glyph, float WorldSize, float ScreenScale, float MaxDistance)
{
    /// <summary>A filled quad <paramref name="metres"/> across, at any distance.</summary>
    public static MarkerStyle World(float metres) => new(MarkerGlyph.Square, metres, 0f, float.MaxValue);

    /// <summary>A glyph holding <paramref name="pixels"/> of viewport height, out to
    /// <paramref name="maxDistance"/> metres. <paramref name="viewportPixels"/> and the camera's
    /// vertical field of view turn that into the per-instance world size the vertex shader needs.
    /// </summary>
    public static MarkerStyle Screen(
        MarkerGlyph glyph, float pixels, float viewportPixels, float verticalFovRadians, float maxDistance)
        => new(glyph, 0f,
            2f * MathF.Tan(verticalFovRadians * 0.5f) * (pixels / MathF.Max(viewportPixels, 1f)),
            maxDistance);
}

/// <summary>
/// Draws every positioned entity as an instanced billboard from the session's pre-baked
/// <c>[x, y, z, r, g, b]</c> stream. The same program serves both viewport modes: 2D passes
/// ground-plane axes and flattens Z; 3D passes the camera's right/up so markers face it.
/// </summary>
public sealed class EntityMarkerLayer : IDisposable
{
    public const int Stride = 6;

    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _instanceBuffer;
    private int _instanceCount;
    private readonly int _uProjection;
    private readonly int _uWorldSize;
    private readonly int _uScreenScale;
    private readonly int _uMaxDistance;
    private readonly int _uCameraPosition;
    private readonly int _uGlyph;
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

        _program = new ShaderProgram(
            $$"""
            #version 330 core
            layout(location = 0) in vec3 center;
            layout(location = 1) in vec3 color;
            uniform mat4 projection;
            uniform vec3 cameraPosition;
            uniform float worldSize;
            uniform float screenScale;
            uniform float maxDistance;
            uniform vec3 right;
            uniform vec3 up;
            uniform float flattenZ;
            {{ShaderProgram.QuadCornerGlsl}}
            out vec3 markerColor;
            out vec2 cell;
            void main()
            {
                vec2 corner = quadCorner() - 0.5;
                vec3 at = vec3(center.xy, center.z * flattenZ);
                // World-sized layers leave screenScale at zero; glyph layers leave worldSize at
                // zero and grow with distance, which is what holds them at a fixed pixel size.
                float viewDistance = distance(cameraPosition, at);
                float size = viewDistance > maxDistance ? 0.0 : worldSize + screenScale * viewDistance;
                gl_Position = projection * vec4(at + (right * corner.x + up * corner.y) * size, 1.0);
                markerColor = color;
                cell = corner;
            }
            """,
            $$"""
            #version 330 core
            in vec3 markerColor;
            in vec2 cell;
            uniform int glyph;
            out vec4 fragment;

            // Signed distances, negative inside. The quad spans -0.5..0.5, so every radius below is
            // a fraction of the glyph's own box and stays put whatever the marker is scaled to.
            float box(vec2 p, vec2 half) {
                vec2 q = abs(p) - half;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }
            float burst(vec2 p) {
                return min(abs(length(p) - 0.32) - 0.07, length(p) - 0.10);
            }
            float cone(vec2 p) {
                return max(-p.y - 0.34, abs(p.x) * 2.0 + p.y - 0.44);
            }
            float doorway(vec2 p) {
                // Open at the bottom: the cut-out reaches past the frame's lower edge.
                return max(box(p, vec2(0.28, 0.40)), -box(p - vec2(0.0, -0.10), vec2(0.17, 0.38)));
            }
            float diamond(vec2 p) {
                return abs(abs(p.x) + abs(p.y) - 0.38) - 0.07;
            }

            void main()
            {
                float alpha = 0.9;
                if (glyph != 0)
                {
                    float d =
                        glyph == 1 ? burst(cell) :
                        glyph == 2 ? cone(cell) :
                        glyph == 3 ? doorway(cell) : diamond(cell);
                    // Softened over roughly a pixel's worth of the glyph, so edges do not crawl.
                    alpha *= 1.0 - smoothstep(-0.015, 0.015, d);
                    if (alpha < 0.01) { discard; }
                }
                fragment = vec4(markerColor, alpha);
            }
            """);
        _uProjection = _program.UniformLocation("projection");
        _uCameraPosition = _program.UniformLocation("cameraPosition");
        _uWorldSize = _program.UniformLocation("worldSize");
        _uScreenScale = _program.UniformLocation("screenScale");
        _uMaxDistance = _program.UniformLocation("maxDistance");
        _uGlyph = _program.UniformLocation("glyph");
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

    public void Draw(
        Matrix4 projection, Vector3 cameraPosition, Vector3 right, Vector3 up, bool flattenZ,
        MarkerStyle style)
    {
        if (_instanceCount == 0)
        {
            return;
        }

        _program.Use();
        GL.UniformMatrix4(_uProjection, false, ref projection);
        GL.Uniform3(_uCameraPosition, cameraPosition);
        GL.Uniform1(_uWorldSize, style.WorldSize);
        GL.Uniform1(_uScreenScale, style.ScreenScale);
        GL.Uniform1(_uMaxDistance, style.MaxDistance);
        GL.Uniform1(_uGlyph, (int)style.Glyph);
        GL.Uniform3(_uRight, right);
        GL.Uniform3(_uUp, up);
        GL.Uniform1(_uFlattenZ, flattenZ ? 0f : 1f);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.BindVertexArray(_vao);
        GL.DrawArraysInstanced(PrimitiveType.Triangles, 0, 6, _instanceCount);
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
        GL.DeleteBuffer(_instanceBuffer);
    }
}
