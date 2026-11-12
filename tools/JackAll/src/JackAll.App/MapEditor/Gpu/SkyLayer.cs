using System.Numerics;
using Vortice.Direct3D;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// A procedural sky: one full-screen triangle whose pixels unproject to a view ray, shaded as a
/// horizon-to-zenith gradient with a sun disc and glow. No geometry, no textures, no cubemap - it
/// replaces the flat clear colour for the cost of three vertices.
/// </summary>
public sealed class SkyLayer : IDisposable
{
    private const string Shared =
        """
        cbuffer Params : register(b0)
        {
            float4x4 inverseViewProjection;
            float3 cameraPosition;
        };

        struct Varyings
        {
            float4 position : SV_Position;
            float2 ndc : TEXCOORD0;
        };
        """;

    private readonly ShaderProgram _program;
    private readonly int _uInverseViewProjection;
    private readonly int _uCameraPosition;

    public SkyLayer()
    {
        _program = new ShaderProgram(
            $$"""
            {{Shared}}
            {{ShaderProgram.FullScreenTriangleHlsl}}
            Varyings main(uint id : SV_VertexID)
            {
                Varyings o;
                o.ndc = screenCorner(id) * 2.0 - 1.0;
                o.position = toTarget(float4(o.ndc, 1.0, 1.0));
                return o;
            }
            """,
            $$"""
            {{Shared}}
            {{SceneLighting.SkyHlsl}}

            float4 main(Varyings v) : SV_Target
            {
                float4 far = mul(inverseViewProjection, float4(v.ndc, 1.0, 1.0));
                float3 ray = normalize(far.xyz / far.w - cameraPosition);

                // The disc's angular radius goes as the inverse square root of the exponent, so
                // halving the sun means four times the exponent, not twice it.
                float3 sky = skyColour(ray, sunDirection)
                           + sunTint * pow(max(dot(ray, sunDirection), 0.0), 7200.0) * 6.0;

                return float4(sky, 1.0);
            }
            """);
        _uInverseViewProjection = _program.Offset("inverseViewProjection");
        _uCameraPosition = _program.Offset("cameraPosition");
    }

    /// <summary>Drawn before the scene with depth off, so it fills whatever the terrain does not
    /// cover without ever occluding it.</summary>
    public void Draw(Matrix4x4 viewProjection, Vector3 cameraPosition)
    {
        Matrix4x4.Invert(viewProjection, out Matrix4x4 inverse);

        using GpuState pass = new();
        _program.Use();
        _program.Set(_uInverseViewProjection, inverse);
        _program.Set(_uCameraPosition, cameraPosition);
        GpuState.Depth(DepthMode.Off);
        _program.Draw(3);
    }

    public void Dispose() => _program.Dispose();
}
