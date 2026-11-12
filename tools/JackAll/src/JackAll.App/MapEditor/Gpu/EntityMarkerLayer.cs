using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

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
/// <c>[x, y, z, r, g, b]</c> stream, each facing the camera along its right and up.
/// </summary>
public sealed class EntityMarkerLayer : IDisposable
{
    public const int Stride = 6;

    private const string Shared =
        """
        cbuffer Params : register(b0)
        {
            float4x4 projection;
            float3 cameraPosition;
            float worldSize;
            float3 right;
            float screenScale;
            float3 up;
            float maxDistance;
            int glyph;
        };

        struct Varyings
        {
            float4 position : SV_Position;
            float3 markerColor : COLOR;
            float2 cell : TEXCOORD0;
        };
        """;

    private static readonly InputElementDescription[] Layout =
    [
        new("INSTANCE_CENTER", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerInstanceData, 1),
        new("INSTANCE_COLOR", 0, Format.R32G32B32_Float, 12, 0, InputClassification.PerInstanceData, 1),
    ];

    private readonly ShaderProgram _program;
    private ID3D11Buffer _instanceBuffer;
    private int _instanceCount;
    private int _capacity;
    private readonly int _uProjection;
    private readonly int _uWorldSize;
    private readonly int _uScreenScale;
    private readonly int _uMaxDistance;
    private readonly int _uCameraPosition;
    private readonly int _uGlyph;
    private readonly int _uRight;
    private readonly int _uUp;

    public EntityMarkerLayer(float[] instances, int instanceCount)
        : this(instanceCount)
        => SetInstances(instances, instanceCount);

    /// <summary>A layer whose stream is refilled via <see cref="SetInstances"/> - the program and
    /// buffer survive every refill, so a rebuild is one write unless it outgrows the buffer.</summary>
    public EntityMarkerLayer(int capacity)
    {
        _capacity = Math.Max(1, capacity);
        _instanceBuffer = NewBuffer(_capacity);

        _program = new ShaderProgram(
            $$"""
            {{Shared}}
            {{ShaderProgram.QuadCornerHlsl}}
            Varyings main(float3 center : INSTANCE_CENTER, float3 color : INSTANCE_COLOR, uint id : SV_VertexID)
            {
                Varyings o;
                float2 corner = quadCorner(id) - 0.5;
                float3 at = center;
                // World-sized layers leave screenScale at zero; glyph layers leave worldSize at
                // zero and grow with distance, which is what holds them at a fixed pixel size.
                float viewDistance = distance(cameraPosition, at);
                float size = viewDistance > maxDistance ? 0.0 : worldSize + screenScale * viewDistance;
                o.position = toTarget(mul(projection, float4(at + (right * corner.x + up * corner.y) * size, 1.0)));
                o.markerColor = color;
                o.cell = corner;
                return o;
            }
            """,
            $$"""
            {{Shared}}

            // Signed distances, negative inside. The quad spans -0.5..0.5, so every radius below is
            // a fraction of the glyph's own box and stays put whatever the marker is scaled to.
            float box(float2 p, float2 half) {
                float2 q = abs(p) - half;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
            }
            float burst(float2 p) {
                return min(abs(length(p) - 0.32) - 0.07, length(p) - 0.10);
            }
            float cone(float2 p) {
                return max(-p.y - 0.34, abs(p.x) * 2.0 + p.y - 0.44);
            }
            float doorway(float2 p) {
                // Open at the bottom: the cut-out reaches past the frame's lower edge.
                return max(box(p, float2(0.28, 0.40)), -box(p - float2(0.0, -0.10), float2(0.17, 0.38)));
            }
            float diamond(float2 p) {
                return abs(abs(p.x) + abs(p.y) - 0.38) - 0.07;
            }

            float4 main(Varyings v) : SV_Target
            {
                float alpha = 0.9;
                if (glyph != 0)
                {
                    float d =
                        glyph == 1 ? burst(v.cell) :
                        glyph == 2 ? cone(v.cell) :
                        glyph == 3 ? doorway(v.cell) : diamond(v.cell);
                    // Softened over roughly a pixel's worth of the glyph, so edges do not crawl.
                    alpha *= 1.0 - smoothstep(-0.015, 0.015, d);
                    if (alpha < 0.01) { discard; }
                }
                return float4(v.markerColor, alpha);
            }
            """,
            Layout);
        _uProjection = _program.Offset("projection");
        _uCameraPosition = _program.Offset("cameraPosition");
        _uWorldSize = _program.Offset("worldSize");
        _uScreenScale = _program.Offset("screenScale");
        _uMaxDistance = _program.Offset("maxDistance");
        _uGlyph = _program.Offset("glyph");
        _uRight = _program.Offset("right");
        _uUp = _program.Offset("up");
    }

    private static ID3D11Buffer NewBuffer(int capacity)
        => Device.CreateBuffer((uint)(capacity * Stride * sizeof(float)), BindFlags.VertexBuffer);

    /// <summary>Rewrites the live instances in place, growing the buffer when they outgrow it.</summary>
    public void SetInstances(float[] instances, int count)
    {
        _instanceCount = count;
        if (count == 0)
        {
            return;
        }

        if (count > _capacity)
        {
            _capacity = count * 2;
            _instanceBuffer.Dispose();
            _instanceBuffer = NewBuffer(_capacity);
        }
        Context.UpdateSubresource(instances.AsSpan(0, count * Stride), _instanceBuffer,
            region: new Vortice.Mathematics.Box(0, 0, 0, count * Stride * sizeof(float), 1, 1));
    }

    public void Draw(
        Matrix4x4 projection, Vector3 cameraPosition, Vector3 right, Vector3 up,
        MarkerStyle style)
    {
        if (_instanceCount == 0)
        {
            return;
        }

        using GpuState pass = new();
        _program.Use();
        _program.Set(_uProjection, projection);
        _program.Set(_uCameraPosition, cameraPosition);
        _program.Set(_uWorldSize, style.WorldSize);
        _program.Set(_uScreenScale, style.ScreenScale);
        _program.Set(_uMaxDistance, style.MaxDistance);
        _program.Set(_uGlyph, (int)style.Glyph);
        _program.Set(_uRight, right);
        _program.Set(_uUp, up);
        GpuState.AlphaBlend(true);
        Context.IASetVertexBuffer(0, _instanceBuffer, Stride * sizeof(float));
        _program.DrawInstanced(6, _instanceCount);
        FrameStats.Drawn(2L * _instanceCount);
    }

    public void Dispose()
    {
        _program.Dispose();
        _instanceBuffer.Dispose();
    }
}
