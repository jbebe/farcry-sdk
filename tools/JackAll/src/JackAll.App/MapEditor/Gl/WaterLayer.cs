using JackAll.Tools.World;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// The water surfaces: one instanced grid per sector that declares water, at the height that sector
/// records. Neighbouring sectors of the same body share a height, so a lake reads as one surface
/// even though it is drawn per sector.
/// </summary>
/// <remarks>
/// What makes it read as water is the volume, not the mirror: how far light gets through before the
/// bottom stops showing. The depth buffer gives that, and the waves, the refraction and the glint
/// all sit on top of it.
/// </remarks>
public sealed class WaterLayer : IDisposable
{
    private const int SectorSize = 64;

    /// <summary>Quads per sector edge. The waves displace vertically only, so a sector's grid stays
    /// inside its own square and meets its neighbour's exactly.</summary>
    private const int GridSide = 24;

    /// <summary>Sectors from the camera that still draw. Past this the surface is haze anyway, and
    /// a sector now costs a grid rather than the two triangles it used to.</summary>
    private const int DrawRadius = 10;

    /// <summary>Floats per instance: origin x y, water level, 1 for a river, then the tint.</summary>
    private const int InstanceStride = 7;

    /// <summary>
    /// Every wave on the surface: direction, wavelength and amplitude in metres, and how much
    /// faster than the base speed it travels. One table, because a normal fitted to different waves
    /// than the geometry carries is a surface that lights wrongly everywhere.
    /// </summary>
    private static readonly (float X, float Y, float Wavelength, float Amplitude, float Speed)[] Waves =
    [
        (0.86f, 0.50f, 11.0f, 0.075f, 1.0f),
        (-0.30f, 0.95f, 6.5f, 0.045f, 1.0f),
        (0.60f, -0.80f, 3.7f, 0.025f, 1.0f),
        (0.95f, 0.31f, 1.6f, 0.011f, 1.6f),
        (-0.70f, 0.71f, 0.9f, 0.005f, 2.1f),
        (0.20f, -0.98f, 0.5f, 0.002f, 2.7f),
    ];

    /// <summary>How many of them the mesh actually carries. The rest are far shorter than a grid
    /// quad, so they live only in the normal - which is per pixel, and stays sharp right up to the
    /// camera without a grid fine enough to hold them.</summary>
    private const int DisplacedWaves = 3;

    /// <summary>
    /// The wave shape, shared by the shader that displaces the surface and the one that shades it.
    /// Both stages paste the whole block; each uses the half it needs.
    /// </summary>
    private static readonly string WaveGlsl =
        $$"""
        uniform float time;

        // River water runs faster than a lake's.
        float waveSpeed(float river)
        {
            return mix(0.35, 1.1, river);
        }

        // Gerstner, vertical component only. The horizontal part sharpens crests by bunching the
        // surface toward them, but it also drags each sector's grid off its own 64 m square and
        // opens a gap against its neighbour - and a gap in a surface that blends without writing
        // depth shows the terrain and the sky straight through it.
        float waveHeight(vec2 position, vec2 direction, float wavelength, float amplitude, float speed)
        {
            float k = 6.2831853 / wavelength;
            return amplitude * sin(k * (dot(direction, position) - speed * time));
        }

        // The slope that same wave carries, so the normal comes from a derivative rather than from
        // differencing a mesh far too coarse to hold the detail.
        vec2 waveSlope(vec2 position, vec2 direction, float wavelength, float amplitude, float speed)
        {
            float k = 6.2831853 / wavelength;
            return direction * (amplitude * k * cos(k * (dot(direction, position) - speed * time)));
        }

        // What the mesh is displaced by.
        float swell(vec2 position, float river)
        {
            float speed = waveSpeed(river);
            return {{WaveTerms("waveHeight", DisplacedWaves)}};
        }

        // What the shading sees: the same swell plus the ripples below grid scale.
        vec3 surfaceNormal(vec2 position, float river)
        {
            float speed = waveSpeed(river);
            vec2 slope = {{WaveTerms("waveSlope", Waves.Length)}};
            return normalize(vec3(-slope, 1.0));
        }
        """;

    /// <summary>The first <paramref name="count"/> waves as a summed GLSL expression. Directions are
    /// normalised here rather than in the shader, where it would be an inverse square root per wave
    /// per fragment to normalise a literal.</summary>
    private static string WaveTerms(string function, int count)
    {
        var terms = new List<string>(count);
        foreach ((float x, float y, float wavelength, float amplitude, float speed) in Waves.Take(count))
        {
            float length = MathF.Sqrt(x * x + y * y);
            terms.Add($"{function}(position, vec2({N(x / length)}, {N(y / length)}), "
                + $"{N(wavelength)}, {N(amplitude)}, speed * {N(speed)})");
        }
        return string.Join("\n                 + ", terms);
    }

    /// <summary>Invariant, so a comma-decimal locale cannot turn one constant into two.</summary>
    private static string N(float value)
        => value.ToString("0.0#####", System.Globalization.CultureInfo.InvariantCulture);

    private readonly ShaderProgram _program;
    private readonly WaterSector[] _sectors;
    private readonly float[] _staging;
    private readonly int _side;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _ebo;
    private readonly int _instanceBuffer;
    private readonly int _indexCount;
    private int _instanceCount;
    private readonly int _uViewProjection;
    private readonly int _uCameraPosition;
    private readonly int _uSunDirection;
    private readonly int _uDemo;
    private readonly int _uTime;
    private readonly int _uFogSetup;
    private readonly int _uFogTint;

    /// <summary>Whether any water is near enough to draw. The scene copy the refraction reads costs
    /// a full-screen colour and depth blit, and on a map with no water near the camera there is
    /// nothing to spend it on.</summary>
    public bool HasVisibleWater => _instanceCount > 0;

    public WaterLayer(WorldTerrain terrain)
    {
        _sectors = [.. terrain.Water];
        _side = terrain.Side / SectorSize;
        _staging = new float[_sectors.Length * InstanceStride];

        _program = new ShaderProgram(
            $$"""
            #version 330 core
            layout(location = 0) in vec2 gridCorner;
            layout(location = 1) in vec4 instanceOrigin;
            layout(location = 2) in vec3 instanceTint;
            uniform mat4 viewProjection;
            {{WaveGlsl}}
            out vec3 worldPosition;
            out vec3 surfaceTint;
            out float riverness;
            void main()
            {
                vec2 plane = instanceOrigin.xy + gridCorner * {{SectorSize}}.0;
                riverness = instanceOrigin.w;
                surfaceTint = instanceTint;
                worldPosition = vec3(plane, instanceOrigin.z + swell(plane, riverness));
                gl_Position = viewProjection * vec4(worldPosition, 1.0);
            }
            """,
            $$"""
            #version 330 core
            in vec3 worldPosition;
            in vec3 surfaceTint;
            in float riverness;
            uniform vec3 cameraPosition;
            uniform vec3 sunDirection;
            uniform float demo;
            uniform sampler2D sceneColour;
            uniform sampler2D sceneDepth;
            out vec4 fragment;

            {{SceneLighting.SkyGlsl}}
            {{WaveGlsl}}

            const float near = {{Camera3D.NearPlane:0.0##}};
            const float far = {{Camera3D.FarPlane:0.0}};

            float linearDepth(float sampled)
            {
                float z = sampled * 2.0 - 1.0;
                return 2.0 * near * far / (far + near - z * (far - near));
            }

            void main()
            {
                vec2 screenUv = gl_FragCoord.xy / vec2(textureSize(sceneDepth, 0));
                float viewDistance = distance(cameraPosition, worldPosition);
                vec3 view = normalize(cameraPosition - worldPosition);
                float surfaceZ = linearDepth(gl_FragCoord.z);

                // Ripples flatten with distance, or the surface turns to noise at the horizon where
                // one pixel spans several wavelengths.
                vec3 normal = normalize(mix(surfaceNormal(worldPosition.xy, riverness), vec3(0.0, 0.0, 1.0),
                                            clamp(viewDistance / 260.0, 0.0, 1.0)));

                // How much water the view travels through before it reaches whatever is behind it.
                float column = max(linearDepth(texture(sceneDepth, screenUv).r) - surfaceZ, 0.0);

                // Refraction, offset by the surface slope and by how much water there is to bend
                // through. A sample that turns out to be in front of the surface is something
                // standing out of the water, so it snaps back rather than being dragged under.
                vec2 offset = normal.xy * min(column, 15.0) * 0.0036;
                vec2 refractUv = clamp(screenUv + offset, vec2(0.001), vec2(0.999));
                if (linearDepth(texture(sceneDepth, refractUv).r) < surfaceZ)
                {
                    refractUv = screenUv;
                }

                // Beer-Lambert through the tint: shallows keep the ground, depth takes it away.
                // This, not the reflection, is what reads as a volume rather than a sheet.
                vec3 extinction = (1.0 - surfaceTint) * 0.9;
                vec3 throughWater = texture(sceneColour, refractUv).rgb * exp(-extinction * column)
                                  + surfaceTint * (1.0 - exp(-column * 0.5)) * 0.35;

                float grazing = 1.0 - max(dot(normal, view), 0.0);
                float grazing2 = grazing * grazing;
                float fresnel = 0.02 + 0.98 * grazing2 * grazing2 * grazing;
                vec3 reflected = skyColour(reflect(-view, normal), sunDirection);

                // A narrow GGX lobe on the rippled normal: the glitter path toward the sun, which is
                // the one highlight a flat sheet can never produce.
                vec3 halfway = normalize(view + sunDirection);
                // GGX, in terms of alpha squared throughout - the roughness itself never appears.
                float alphaSq = 9.150625e-6;
                float ndoth = max(dot(normal, halfway), 0.0);
                float denominator = ndoth * ndoth * (alphaSq - 1.0) + 1.0;
                float glint = alphaSq / max(3.14159265 * denominator * denominator, 1.0e-5);

                // Foam where the water is shallow enough to break, plus a thread along the crests.
                float shore = 1.0 - smoothstep(0.0, 1.2, column);
                float crest = smoothstep(0.55, 1.0, length(normal.xy) * 9.0);
                float foam = clamp(shore + crest * 0.35, 0.0, 1.0)
                           * (1.0 - clamp(viewDistance / 400.0, 0.0, 1.0));

                vec3 lit = mix(throughWater, reflected, fresnel) + sunTint * min(glint, 60.0) * 0.5;
                lit = mix(lit, vec3(0.72), foam);
                lit = applyHaze(lit, viewDistance, worldPosition.z, demo);

                // Switched off, water falls back to the flat translucent tint - the readable form
                // for judging where water is rather than how it looks. On, it covers what is behind
                // it, because the refraction already brought that through.
                fragment = vec4(mix(surfaceTint, lit, demo),
                                mix(0.55, clamp(1.0 - exp(-column * 3.0) + fresnel, 0.0, 1.0), demo));
            }
            """);
        _uViewProjection = _program.UniformLocation("viewProjection");
        _uCameraPosition = _program.UniformLocation("cameraPosition");
        _uSunDirection = _program.UniformLocation("sunDirection");
        _uDemo = _program.UniformLocation("demo");
        _uTime = _program.UniformLocation("time");
        _uFogSetup = _program.UniformLocation("fogSetup");
        _uFogTint = _program.UniformLocation("fogTint");
        _program.Use();
        GL.Uniform1(_program.UniformLocation("sceneColour"), 0);
        GL.Uniform1(_program.UniformLocation("sceneDepth"), 1);

        // One grid, drawn once per water sector - the same shape the entity layer already instances
        // with, in place of six vertices per sector written into one buffer.
        var corners = new float[(GridSide + 1) * (GridSide + 1) * 2];
        int at = 0;
        for (int y = 0; y <= GridSide; y++)
        {
            for (int x = 0; x <= GridSide; x++)
            {
                corners[at++] = x / (float)GridSide;
                corners[at++] = y / (float)GridSide;
            }
        }

        var indices = new int[GridSide * GridSide * 6];
        at = 0;
        for (int y = 0; y < GridSide; y++)
        {
            for (int x = 0; x < GridSide; x++)
            {
                int origin = y * (GridSide + 1) + x;
                indices[at++] = origin;
                indices[at++] = origin + 1;
                indices[at++] = origin + GridSide + 1;
                indices[at++] = origin + 1;
                indices[at++] = origin + GridSide + 2;
                indices[at++] = origin + GridSide + 1;
            }
        }
        _indexCount = indices.Length;

        _vao = GL.GenVertexArray();
        GL.BindVertexArray(_vao);

        _vbo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, corners.Length * sizeof(float), corners,
            BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);

        _ebo = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(int), indices,
            BufferUsageHint.StaticDraw);

        _instanceBuffer = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceBuffer);
        GL.BufferData(BufferTarget.ArrayBuffer, Math.Max(_staging.Length, 1) * sizeof(float),
            IntPtr.Zero, BufferUsageHint.DynamicDraw);
        int stride = InstanceStride * sizeof(float);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 4, VertexAttribPointerType.Float, false, stride, 0);
        GL.VertexAttribDivisor(1, 1);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
        GL.VertexAttribDivisor(2, 1);
        GL.BindVertexArray(0);
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

    /// <summary>
    /// Refills the instance stream with the sectors near the camera, on the same sector-crossing
    /// trigger the scatter rebuilds on. A grid per sector across a whole map is far more surface
    /// than the two triangles apiece this replaced, so the far ones have to go.
    /// </summary>
    public void SetVisible(Vector3 cameraPosition)
    {
        int cameraX = (int)MathF.Floor(cameraPosition.X / SectorSize);
        int cameraY = (int)MathF.Floor(cameraPosition.Y / SectorSize);

        int at = 0;
        foreach (WaterSector water in _sectors)
        {
            int x = water.SectorId % _side;
            int y = water.SectorId / _side;
            if (Math.Abs(x - cameraX) > DrawRadius || Math.Abs(y - cameraY) > DrawRadius)
            {
                continue;
            }

            (float r, float g, float b) = TintFor(water);
            _staging[at++] = x * SectorSize;
            _staging[at++] = y * SectorSize;
            _staging[at++] = water.Level;
            _staging[at++] = water.River ? 1f : 0f;
            _staging[at++] = r;
            _staging[at++] = g;
            _staging[at++] = b;
        }

        _instanceCount = at / InstanceStride;
        if (_instanceCount == 0)
        {
            return;
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceBuffer);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, at * sizeof(float), _staging);
    }

    public void Draw(Matrix4 viewProjection, Vector3 cameraPosition, float demo, RenderTargets targets)
    {
        if (_instanceCount == 0)
        {
            return;
        }

        _program.Use();
        GL.UniformMatrix4(_uViewProjection, false, ref viewProjection);
        GL.Uniform3(_uCameraPosition, cameraPosition);
        GL.Uniform3(_uSunDirection, SceneLighting.SunDirection);
        GL.Uniform1(_uDemo, demo);
        GL.Uniform1(_uTime, SceneLighting.Time);
        SceneLighting.SetFogUniforms(_uFogSetup, _uFogTint);

        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, targets.DepthCopy);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, targets.ColourCopy);

        GL.BindVertexArray(_vao);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        // Depth-tested against the terrain but not written, so overlapping surfaces don't punch
        // holes in each other.
        GL.DepthMask(false);
        GL.DrawElementsInstanced(PrimitiveType.Triangles, _indexCount, DrawElementsType.UnsignedInt,
            IntPtr.Zero, _instanceCount);
        GL.DepthMask(true);
        GL.Disable(EnableCap.Blend);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        _program.Dispose();
        GL.DeleteBuffer(_vbo);
        GL.DeleteBuffer(_ebo);
        GL.DeleteBuffer(_instanceBuffer);
        GL.DeleteVertexArray(_vao);
    }
}
