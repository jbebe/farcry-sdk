using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The water surfaces: one flat quad per sector that declares water, at the height that sector
/// records. Neighbouring sectors of the same body share a height, so a lake reads as one surface
/// even though it is drawn per sector.
/// </summary>
public sealed class WaterLayer : IDisposable
{
    private const int SectorSize = 64;

    private readonly ShaderProgram _program;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _vertexCount;
    private readonly int _uViewProjection;
    private readonly int _uCameraPosition;
    private readonly int _uSunDirection;
    private readonly int _uDemo;

    public int SectorCount { get; }

    public WaterLayer(WorldTerrain terrain)
    {
        SectorCount = terrain.Water.Count;

        // Two triangles per sector, each vertex carrying its position and a tint chosen by material.
        var vertices = new float[terrain.Water.Count * 6 * 6];
        int cursor = 0;
        foreach (WaterSector water in terrain.Water)
        {
            int side = terrain.Side / SectorSize;
            float x0 = water.SectorId % side * SectorSize;
            float y0 = water.SectorId / side * SectorSize;
            (float r, float g, float b) = TintFor(water);

            Span<(float X, float Y)> corners =
            [
                (x0, y0), (x0 + SectorSize, y0), (x0, y0 + SectorSize),
                (x0 + SectorSize, y0), (x0 + SectorSize, y0 + SectorSize), (x0, y0 + SectorSize),
            ];
            foreach ((float x, float y) in corners)
            {
                vertices[cursor++] = x;
                vertices[cursor++] = y;
                vertices[cursor++] = water.Level;
                vertices[cursor++] = r;
                vertices[cursor++] = g;
                vertices[cursor++] = b;
            }
        }
        _vertexCount = terrain.Water.Count * 6;

        _program = new ShaderProgram(
            """
            #version 330 core
            layout(location = 0) in vec3 position;
            layout(location = 1) in vec3 tint;
            uniform mat4 viewProjection;
            out vec3 surfaceTint;
            out vec3 worldPosition;
            void main()
            {
                surfaceTint = tint;
                worldPosition = position;
                gl_Position = viewProjection * vec4(position, 1.0);
            }
            """,
            $$"""
            #version 330 core
            in vec3 surfaceTint;
            in vec3 worldPosition;
            uniform vec3 cameraPosition;
            uniform vec3 sunDirection;
            uniform float demo;
            out vec4 fragment;

            {{SceneLighting.SkyGlsl}}

            void main()
            {
                float viewDistance = distance(cameraPosition, worldPosition);
                vec3 normal = vec3(0.0, 0.0, 1.0);
                vec3 view = normalize(cameraPosition - worldPosition);
                vec3 mirrored = reflect(-view, normal);

                // Fresnel: nearly transparent looking straight down, a mirror at a grazing angle.
                // Carrying it into the alpha as well is most of why this reads as water.
                float fresnel = 0.02 + 0.98 * pow(1.0 - max(dot(normal, view), 0.0), 5.0);

                vec3 reflected = skyColour(mirrored, sunDirection);
                float glint = pow(max(dot(mirrored, sunDirection), 0.0), 220.0);

                vec3 lit = mix(surfaceTint * 0.8, reflected, fresnel) + sunTint * glint * 1.6;
                lit = applyHaze(lit, viewDistance, worldPosition.z, demo);

                // Switched off, water falls back to the flat translucent tint - the readable form
                // for judging where water is rather than how it looks.
                fragment = vec4(mix(surfaceTint, lit, demo), mix(0.55, mix(0.55, 0.95, fresnel), demo));
            }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uCameraPosition = _program.UniformLocation("cameraPosition");
        _uSunDirection = _program.UniformLocation("sunDirection");
        _uDemo = _program.UniformLocation("demo");

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);
        _vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));
    }

    /// <summary>Mossy water reads green, ordinary water blue; rivers sit slightly lighter than lakes.</summary>
    private static (float R, float G, float B) TintFor(WaterSector water)
    {
        bool moss = water.Material.Contains("moss", StringComparison.OrdinalIgnoreCase);
        float lift = water.River ? 0.08f : 0f;
        return moss
            ? (0.18f + lift, 0.34f + lift, 0.20f + lift)
            : (0.12f + lift, 0.26f + lift, 0.38f + lift);
    }

    public void Draw(Matrix4 viewProjection, Vector3 cameraPosition, float demo)
    {
        if (_vertexCount == 0)
        {
            return;
        }

        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        GL.Uniform3(_uCameraPosition, cameraPosition);
        GL.Uniform3(_uSunDirection, SceneLighting.SunDirection);
        GL.Uniform1(_uDemo, demo);
        GL.BindVertexArray(_vao);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        // Depth-tested against the terrain but not written, so overlapping surfaces don't punch
        // holes in each other.
        GL.DepthMask(false);
        GL.DrawArrays(PrimitiveType.Triangles, 0, _vertexCount);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
    }
}

