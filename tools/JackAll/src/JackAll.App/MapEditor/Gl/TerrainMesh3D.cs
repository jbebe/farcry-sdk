using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The 3D terrain: two camera-following grid patches (a fine 1-unit-spacing ring and a coarse
/// 8-unit world backdrop) whose vertex shader pulls heights straight from the shared height
/// texture - no terrain geometry is ever built on the CPU, and edits only need a texture update.
/// Shading is per-fragment: finite-difference normals from the same texture, one sun.
/// </summary>
public sealed class TerrainMesh3D : IDisposable
{
    /// <summary>Vertices per patch edge; 257x257 keeps both patches under half a million triangles.</summary>
    private const int PatchSide = 257;

    private readonly HeightTexture _heights;
    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _indexCount;
    private readonly int _uViewProjection;
    private readonly int _uOrigin;
    private readonly int _uSpacing;

    public TerrainMesh3D(HeightTexture heights)
    {
        _heights = heights;

        string constants =
            $"""
            const float extent = {heights.Side - 1}.0;
            const float metersPerRaw = 65535.0 / 128.0;
            const int patchSide = {PatchSide};
            """;
        _program = new ShaderProgram(
            $$"""
            #version 330 core
            uniform sampler2D heights;
            uniform mat4 viewProjection;
            uniform vec2 origin;
            uniform float spacing;
            {{constants}}
            out vec2 world;
            void main()
            {
                int row = gl_VertexID / patchSide;
                int col = gl_VertexID % patchSide;
                world = origin + vec2(col, row) * spacing;
                vec2 clamped = clamp(world, vec2(0.0), vec2(extent));
                float z = texture(heights, clamped / extent).r * metersPerRaw;
                gl_Position = viewProjection * vec4(clamped, z, 1.0);
            }
            """,
            $$"""
            #version 330 core
            uniform sampler2D heights;
            uniform vec2 heightRange;
            in vec2 world;
            out vec4 fragment;
            {{constants}}
            void main()
            {
                vec2 uv = world / extent;
                float texel = 1.0 / extent;
                float hl = texture(heights, uv - vec2(texel, 0.0)).r;
                float hr = texture(heights, uv + vec2(texel, 0.0)).r;
                float hd = texture(heights, uv - vec2(0.0, texel)).r;
                float hu = texture(heights, uv + vec2(0.0, texel)).r;
                vec3 normal = normalize(vec3((hl - hr) * metersPerRaw, (hd - hu) * metersPerRaw, 2.0));

                float h = texture(heights, uv).r;
                float shade = (h - heightRange.x) / (heightRange.y - heightRange.x);
                vec3 base = mix(vec3(0.24, 0.30, 0.18), vec3(0.62, 0.56, 0.44), shade);
                float light = max(dot(normal, normalize(vec3(0.4, 0.3, 0.85))), 0.0);
                fragment = vec4(base * (0.35 + 0.65 * light), 1.0);
            }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uOrigin = _program.UniformLocation("origin");
        _uSpacing = _program.UniformLocation("spacing");
        _program.Use();
        GL.Uniform2(_program.UniformLocation("heightRange"), heights.MinNormalized, heights.MaxNormalized);

        // Positions come from gl_VertexID; only the triangulation needs real data.
        var indices = new int[(PatchSide - 1) * (PatchSide - 1) * 6];
        int cursor = 0;
        for (int row = 0; row < PatchSide - 1; row++)
        {
            for (int col = 0; col < PatchSide - 1; col++)
            {
                int v = row * PatchSide + col;
                indices[cursor++] = v;
                indices[cursor++] = v + 1;
                indices[cursor++] = v + PatchSide;
                indices[cursor++] = v + 1;
                indices[cursor++] = v + PatchSide + 1;
                indices[cursor++] = v + PatchSide;
            }
        }
        _indexCount = indices.Length;

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        int ebo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices, BufferUsageHint.StaticDraw);
    }

    public void Draw(Matrix4 viewProjection, Vector3 cameraPosition)
    {
        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        _heights.Bind(TextureUnit.Texture0);
        GL.BindVertexArray(_vao);
        GL.Enable(EnableCap.DepthTest);

        // Coarse whole-world backdrop, then the fine ring around the camera drawn over it; slight
        // depth offset on the backdrop avoids z-fighting where they overlap.
        GL.Enable(EnableCap.PolygonOffsetFill);
        GL.PolygonOffset(1f, 1f);
        DrawPatch(spacing: (_heights.Side - 1f) / (PatchSide - 1), originX: 0, originY: 0);
        GL.Disable(EnableCap.PolygonOffsetFill);

        const float fineSpacing = 1f;
        float half = (PatchSide - 1) * fineSpacing / 2f;
        // Snapped to whole units so the fine grid doesn't swim as the camera pans.
        float ox = MathF.Floor(cameraPosition.X - half);
        float oy = MathF.Floor(cameraPosition.Y - half);
        DrawPatch(fineSpacing, ox, oy);
    }

    private void DrawPatch(float spacing, float originX, float originY)
    {
        GL.Uniform2(_uOrigin, originX, originY);
        GL.Uniform1(_uSpacing, spacing);
        GL.DrawElements(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt, 0);
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteVertexArray(_vao);
    }
}
