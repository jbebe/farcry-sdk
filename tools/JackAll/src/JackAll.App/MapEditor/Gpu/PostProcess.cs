using Vortice.Direct3D;
using Vortice.Direct3D11;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// The step from scene radiance to a displayable image: exposure, a tonemap, and the sRGB encode.
/// Exposure lives here rather than in each layer, which is what stops the sky and the water drifting
/// out of step with the ground the way they did while every shader applied it for itself.
/// </summary>
public sealed class PostProcess : IDisposable
{
    private readonly ShaderProgram _program;
    private readonly int _uExposure;

    public PostProcess()
    {
        _program = new ShaderProgram(
            ShaderProgram.FullScreenTriangleVertex,
            $$"""
            {{ShaderProgram.ScreenVaryingsHlsl}}
            Texture2D scene : register(t0);
            cbuffer Params : register(b0)
            {
                float exposure;
            };

            {{SceneLighting.ColourHlsl}}

            // Khronos PBR Neutral. Highlights roll off to white while anything in gamut keeps its
            // hue and saturation - ACES tilts bright colour toward its primaries and turns a blown
            // sky cyan, which is a film-stock look laid over the data rather than a neutral one.
            float3 tonemap(float3 colour)
            {
                const float startCompression = 0.8 - 0.04;
                const float desaturation = 0.15;

                float darkest = min(colour.r, min(colour.g, colour.b));
                float offset = darkest < 0.08 ? darkest - 6.25 * darkest * darkest : 0.04;
                colour -= offset;

                float peak = max(colour.r, max(colour.g, colour.b));
                if (peak < startCompression)
                {
                    return colour;
                }

                float d = 1.0 - startCompression;
                float newPeak = 1.0 - d * d / (peak + d - startCompression);
                colour *= newPeak / peak;
                return lerp(colour, newPeak,
                            1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0));
            }

            {{ShaderProgram.NoiseHlsl}}

            float4 main(ScreenVaryings v) : SV_Target
            {
                float3 colour = scene.SampleLevel(linearClamp, v.uv, 0).rgb * exposure;

                // Switched off, the tonemap and the dither go with it: a flat view of the data wants
                // the values it was given, clipped, and there is no sky gradient left to band.
                if (demo < 0.5)
                {
                    return float4(linearToSrgb(saturate(colour)), 1.0);
                }

                float3 encoded = linearToSrgb(saturate(tonemap(colour)));
                // Triangular PDF at one 8-bit step, which is what keeps the sky gradient from
                // banding once it is quantised.
                float dither = (interleavedGradientNoise(v.position.xy)
                              + interleavedGradientNoise(v.position.xy + 17.0) - 1.0) / 255.0;
                return float4(encoded + dither, 1.0);
            }
            """);
        _uExposure = _program.Offset("exposure");
    }

    /// <summary>Resolves the scene colour into whatever target is bound. Depth is left alone: the
    /// target shares the scene's depth, and the overlays drawn afterwards test against it.</summary>
    public void Composite(ID3D11ShaderResourceView sceneColour)
    {
        using GpuState pass = new();
        _program.Use();
        _program.Set(_uExposure, SceneLighting.Exposure);
        Context.PSSetShaderResource(0, sceneColour);
        GpuState.Depth(DepthMode.Off);
        _program.Draw(3);
    }

    public void Dispose() => _program.Dispose();
}

/// <summary>
/// The last pass of a frame: the 8-bit present buffer onto the swap chain's back buffer, the one
/// draw that turns the bottom-up image the other passes made the right way up.
/// </summary>
public sealed class PresentPass : IDisposable
{
    private readonly ShaderProgram _program = new(
        $$"""
        {{ShaderProgram.FullScreenTriangleHlsl}}
        {{ShaderProgram.ScreenVaryingsHlsl}}
        ScreenVaryings main(uint id : SV_VertexID)
        {
            ScreenVaryings o;
            o.uv = screenCorner(id);
            o.position = float4(o.uv * 2.0 - 1.0, 0.0, 1.0);
            return o;
        }
        """,
        $$"""
        {{ShaderProgram.ScreenVaryingsHlsl}}
        Texture2D image : register(t0);
        float4 main(ScreenVaryings v) : SV_Target
        {
            return image.Load(int3(v.position.x, viewportSize.y - v.position.y, 0));
        }
        """);

    public void Draw(ID3D11RenderTargetView backBuffer, ID3D11ShaderResourceView image, int width, int height)
    {
        using GpuState pass = new();
        Context.OMSetRenderTargets(backBuffer);
        Context.RSSetViewport(0, 0, width, height);
        GpuState.Depth(DepthMode.Off);
        _program.Use();
        Context.PSSetShaderResource(0, image);
        _program.Draw(3);
    }

    public void Dispose() => _program.Dispose();
}
