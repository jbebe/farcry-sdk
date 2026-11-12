using JackAll.Tools.World;
using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

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
    private static readonly string WaveHlsl =
        $$"""


        // River water runs faster than a lake's.
        float waveSpeed(float river)
        {
            return lerp(0.35, 1.1, river);
        }

        // Gerstner, vertical component only. The horizontal part sharpens crests by bunching the
        // surface toward them, but it also drags each sector's grid off its own 64 m square and
        // opens a gap against its neighbour - and a gap in a surface that blends without writing
        // depth shows the terrain and the sky straight through it.
        float waveHeight(float2 position, float2 direction, float wavelength, float amplitude, float speed)
        {
            float k = 6.2831853 / wavelength;
            return amplitude * sin(k * (dot(direction, position) - speed * time));
        }

        // The slope that same wave carries, so the normal comes from a derivative rather than from
        // differencing a mesh far too coarse to hold the detail.
        float2 waveSlope(float2 position, float2 direction, float wavelength, float amplitude, float speed)
        {
            float k = 6.2831853 / wavelength;
            return direction * (amplitude * k * cos(k * (dot(direction, position) - speed * time)));
        }

        // What the mesh is displaced by.
        float swell(float2 position, float river)
        {
            float speed = waveSpeed(river);
            return {{WaveTerms("waveHeight", DisplacedWaves)}};
        }

        // What the shading sees: the same swell plus the ripples below grid scale.
        float3 surfaceNormal(float2 position, float river)
        {
            float speed = waveSpeed(river);
            float2 slope = {{WaveTerms("waveSlope", Waves.Length)}};
            return normalize(float3(-slope, 1.0));
        }
        """;

    /// <summary>The first <paramref name="count"/> waves as a summed HLSL expression. Directions are
    /// normalised here rather than in the shader, where it would be an inverse square root per wave
    /// per fragment to normalise a literal.</summary>
    private static string WaveTerms(string function, int count)
    {
        var terms = new List<string>(count);
        foreach ((float x, float y, float wavelength, float amplitude, float speed) in Waves.Take(count))
        {
            float length = MathF.Sqrt(x * x + y * y);
            terms.Add($"{function}(position, float2({ShaderProgram.Literal(x / length)}, {ShaderProgram.Literal(y / length)}), "
                + $"{ShaderProgram.Literal(wavelength)}, {ShaderProgram.Literal(amplitude)}, speed * {ShaderProgram.Literal(speed)})");
        }
        return string.Join("\n                 + ", terms);
    }

    private const string Shared =
        """
        cbuffer Params : register(b0)
        {
            float4x4 viewProjection;
            float3 cameraPosition;
            float time;
        };

        struct Varyings
        {
            float4 position : SV_Position;
            float3 worldPosition : TEXCOORD0;
            float3 surfaceTint : COLOR;
            float riverness : TEXCOORD1;
        };
        """;

    private static readonly InputElementDescription[] Layout =
    [
        new("POSITION", 0, Format.R32G32_Float, 0, 0),
        new("INSTANCE_ORIGIN", 0, Format.R32G32B32A32_Float, 0, 1, InputClassification.PerInstanceData, 1),
        new("INSTANCE_TINT", 0, Format.R32G32B32_Float, 16, 1, InputClassification.PerInstanceData, 1),
    ];

    private readonly ShaderProgram _program;
    private readonly WaterSector[] _sectors;
    private readonly float[] _staging;
    private readonly int _side;
    private readonly ID3D11Buffer _corners;
    private readonly ID3D11Buffer _indices;
    private readonly ID3D11Buffer _instanceBuffer;
    private readonly ID3D11Buffer[] _streams;
    private static readonly uint[] Strides = [2 * sizeof(float), InstanceStride * sizeof(float)];
    private static readonly uint[] Offsets = [0, 0];
    private readonly int _indexCount;
    private int _instanceCount;
    private readonly int _uViewProjection;
    private readonly int _uCameraPosition;
    private readonly int _uTime;

    /// <summary>Whether any water is near enough to draw. The scene copy the refraction reads costs
    /// a full-screen colour and depth copy, and on a map with no water near the camera there is
    /// nothing to spend it on.</summary>
    public bool HasVisibleWater => _instanceCount > 0;

    public WaterLayer(WorldTerrain terrain)
    {
        _sectors = [.. terrain.Water];
        _side = terrain.Side / SectorSize;
        _staging = new float[_sectors.Length * InstanceStride];

        _program = new ShaderProgram(
            $$"""
            {{Shared}}
            {{WaveHlsl}}
            struct VertexInput
            {
                float2 gridCorner : POSITION;
                float4 instanceOrigin : INSTANCE_ORIGIN;
                float3 instanceTint : INSTANCE_TINT;
            };
            Varyings main(VertexInput i)
            {
                Varyings o;
                float2 plane = i.instanceOrigin.xy + i.gridCorner * {{SectorSize}}.0;
                o.riverness = i.instanceOrigin.w;
                o.surfaceTint = i.instanceTint;
                float height = demo < 0.5 ? 0.0 : swell(plane, o.riverness);
                o.worldPosition = float3(plane, i.instanceOrigin.z + height);
                o.position = toTarget(mul(viewProjection, float4(o.worldPosition, 1.0)));
                return o;
            }
            """,
            $$"""
            {{Shared}}
            Texture2D sceneColour : register(t0);
            Texture2D sceneDepth : register(t1);

            {{SceneLighting.SkyHlsl}}
            {{WaveHlsl}}

            static const float near = {{ShaderProgram.Literal(Camera3D.NearPlane)}};
            static const float far = {{ShaderProgram.Literal(Camera3D.FarPlane)}};

            // From the stored depth, which runs 0 to 1 from the near plane to the far one.
            float linearDepth(float sampled)
            {
                float z = sampled * 2.0 - 1.0;
                return 2.0 * near * far / (far + near - z * (far - near));
            }

            float4 main(Varyings v) : SV_Target
            {
                // Presentation off: a flat translucent pane of the authored tint. Everything below
                // - the wave normal, the three scene taps, the refraction, the glint and the foam -
                // exists only to make it look like water, and the scene copy that feeds it is not
                // even taken.
                if (demo < 0.5)
                {
                    return float4(v.surfaceTint, 0.55);
                }

                float2 size;
                sceneDepth.GetDimensions(size.x, size.y);
                float2 screenUv = v.position.xy / size;
                float viewDistance = distance(cameraPosition, v.worldPosition);
                float3 view = normalize(cameraPosition - v.worldPosition);
                float surfaceZ = linearDepth(v.position.z);

                // Ripples flatten with distance, or the surface turns to noise at the horizon where
                // one pixel spans several wavelengths.
                float3 normal = normalize(lerp(surfaceNormal(v.worldPosition.xy, v.riverness), float3(0.0, 0.0, 1.0),
                                               saturate(viewDistance / 260.0)));

                // How much water the view travels through before it reaches whatever is behind it.
                float column = max(linearDepth(sceneDepth.SampleLevel(pointClamp, screenUv, 0).r) - surfaceZ, 0.0);

                // Refraction, offset by the surface slope and by how much water there is to bend
                // through. A sample that turns out to be in front of the surface is something
                // standing out of the water, so it snaps back rather than being dragged under.
                float2 offset = normal.xy * min(column, 15.0) * 0.0036;
                float2 refractUv = clamp(screenUv + offset, 0.001, 0.999);
                if (linearDepth(sceneDepth.SampleLevel(pointClamp, refractUv, 0).r) < surfaceZ)
                {
                    refractUv = screenUv;
                }

                // Beer-Lambert through the tint: shallows keep the ground, depth takes it away.
                // This, not the reflection, is what reads as a volume rather than a sheet.
                float3 extinction = (1.0 - v.surfaceTint) * 0.9;
                float3 throughWater = sceneColour.SampleLevel(linearClamp, refractUv, 0).rgb * exp(-extinction * column)
                                    + v.surfaceTint * (1.0 - exp(-column * 0.5)) * 0.35;

                float grazing = 1.0 - max(dot(normal, view), 0.0);
                float grazing2 = grazing * grazing;
                float fresnel = 0.02 + 0.98 * grazing2 * grazing2 * grazing;
                float3 reflected = skyColour(reflect(-view, normal), sunDirection);

                // A narrow GGX lobe on the rippled normal: the glitter path toward the sun, which is
                // the one highlight a flat sheet can never produce.
                float3 halfway = normalize(view + sunDirection);
                // GGX, in terms of alpha squared throughout - the roughness itself never appears.
                float alphaSq = 9.150625e-6;
                float ndoth = max(dot(normal, halfway), 0.0);
                float denominator = ndoth * ndoth * (alphaSq - 1.0) + 1.0;
                float glint = alphaSq / max(3.14159265 * denominator * denominator, 1.0e-5);

                // Foam where the water is shallow enough to break, plus a thread along the crests.
                float shore = 1.0 - smoothstep(0.0, 1.2, column);
                float crest = smoothstep(0.55, 1.0, length(normal.xy) * 9.0);
                float foam = saturate(shore + crest * 0.35)
                           * (1.0 - saturate(viewDistance / 400.0));

                float3 lit = lerp(throughWater, reflected, fresnel) + sunTint * min(glint, 60.0) * 0.5;
                lit = lerp(lit, 0.72, foam);
                lit = applyHaze(lit, viewDistance, v.worldPosition.z);

                // It covers what is behind it, because the refraction already brought that through.
                return float4(lit, saturate(1.0 - exp(-column * 3.0) + fresnel));
            }
            """,
            Layout);
        _uViewProjection = _program.Offset("viewProjection");
        _uCameraPosition = _program.Offset("cameraPosition");
        _uTime = _program.Offset("time");

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

        var indices = new uint[GridSide * GridSide * 6];
        at = 0;
        for (int y = 0; y < GridSide; y++)
        {
            for (int x = 0; x < GridSide; x++)
            {
                uint origin = (uint)(y * (GridSide + 1) + x);
                indices[at++] = origin;
                indices[at++] = origin + 1;
                indices[at++] = origin + GridSide + 1;
                indices[at++] = origin + 1;
                indices[at++] = origin + GridSide + 2;
                indices[at++] = origin + GridSide + 1;
            }
        }
        _indexCount = indices.Length;

        _corners = Device.CreateBuffer(corners, BindFlags.VertexBuffer, ResourceUsage.Immutable);
        _indices = Device.CreateBuffer(indices, BindFlags.IndexBuffer, ResourceUsage.Immutable);
        _instanceBuffer = Device.CreateBuffer((uint)(Math.Max(_staging.Length, 1) * sizeof(float)), BindFlags.VertexBuffer);
        _streams = [_corners, _instanceBuffer];
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

        Context.UpdateSubresource(_staging.AsSpan(0, at), _instanceBuffer,
            region: new Vortice.Mathematics.Box(0, 0, 0, at * sizeof(float), 1, 1));
    }

    public void Draw(Matrix4x4 viewProjection, Vector3 cameraPosition, RenderTargets targets)
    {
        if (_instanceCount == 0)
        {
            return;
        }

        using GpuState pass = new();
        _program.Use();
        _program.Set(_uViewProjection, viewProjection);
        _program.Set(_uCameraPosition, cameraPosition);
        _program.Set(_uTime, SceneLighting.Time);

        // Both are null with the presentation off, which is what the flat pane wants: it samples
        // neither, and there is nothing allocated to sample.
        Context.PSSetShaderResource(0, targets.Presentation?.ColourCopy.View!);
        Context.PSSetShaderResource(1, targets.Presentation?.DepthCopy.View!);

        Context.IASetVertexBuffers(0, 2, _streams, Strides, Offsets);
        Context.IASetIndexBuffer(_indices, Format.R32_UInt, 0);
        GpuState.AlphaBlend(true);
        // Depth-tested against the terrain but not written, so overlapping surfaces don't punch
        // holes in each other.
        GpuState.Depth(DepthMode.TestOnly);
        _program.DrawIndexedInstanced(_indexCount, _instanceCount);
        FrameStats.Drawn((long)_indexCount / 3 * _instanceCount);
    }

    public void Dispose()
    {
        _program.Dispose();
        _corners.Dispose();
        _indices.Dispose();
        _instanceBuffer.Dispose();
    }
}
