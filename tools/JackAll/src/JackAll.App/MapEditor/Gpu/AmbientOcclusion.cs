using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// Screen-space ambient occlusion over the depth prepass, and the blur that makes it usable. What
/// it produces multiplies the ambient term and nothing else - darkening a sunlit face with it would
/// be the same mistake as painting shadow into an albedo.
/// </summary>
public sealed class AmbientOcclusion : IDisposable
{
    /// <summary>How far a sample reaches, in metres. Roughly a doorway: wide enough to darken under
    /// eaves and into corners, tight enough that a hillside does not occlude the valley.</summary>
    private const float Radius = 2.2f;

    private readonly ShaderProgram _occlusion;
    private readonly ShaderProgram _blur;
    private readonly int _uInverseProjection;
    private readonly int _bDirection;

    public AmbientOcclusion()
    {
        _occlusion = new ShaderProgram(
            ShaderProgram.FullScreenTriangleVertex,
            $$"""
            {{ShaderProgram.ScreenVaryingsHlsl}}
            Texture2D sceneDepth : register(t0);
            cbuffer Params : register(b0)
            {
                float4x4 inverseProjection;
            };

            static const float radius = {{ShaderProgram.Literal(Radius)}};
            // Constant for the process, so it is folded in rather than uploaded every frame.
            static const float focalLength = {{ShaderProgram.Literal(1f / MathF.Tan(Camera3D.VerticalFovRadians * 0.5f))}};
            static const int directions = 4;
            static const int steps = 6;

            {{ShaderProgram.NoiseHlsl}}

            // Takes the depth rather than sampling it, so the caller that already has it - which is
            // every caller - does not fetch the same texel twice. Depth already runs 0 to 1 in clip.
            float3 viewPosition(float2 texcoord, float depth)
            {
                float4 clip = float4(texcoord * 2.0 - 1.0, depth, 1.0);
                float4 view = mul(inverseProjection, clip);
                return view.xyz / view.w;
            }

            float main(ScreenVaryings v) : SV_Target
            {
                float depth = sceneDepth.SampleLevel(pointClamp, v.uv, 0).r;
                if (depth >= 1.0)
                {
                    return 1.0;
                }

                float2 screen;
                sceneDepth.GetDimensions(screen.x, screen.y);
                float3 origin = viewPosition(v.uv, depth);

                // Taken from the depth buffer rather than a normal target: one less full-screen
                // surface to write, and the derivative is exact everywhere but a silhouette.
                float3 normal = normalize(cross(ddx(origin), ddy(origin)));
                if (normal.z < 0.0)
                {
                    normal = -normal;
                }

                // The view-space radius, in pixels, at this depth.
                float pixelRadius = radius * focalLength * 0.5 * screen.y / max(-origin.z, 0.01);
                pixelRadius = min(pixelRadius, 96.0);

                float occlusion = 0.0;
                float rotation = interleavedGradientNoise(v.position.xy) * 6.2831853;
                [unroll] for (int d = 0; d < directions; d++)
                {
                    float angle = rotation + float(d) * (6.2831853 / float(directions));
                    float2 march = float2(cos(angle), sin(angle));

                    // The highest thing this direction can see, as the sine of its elevation over
                    // the surface. Occlusion is how far up the hemisphere that reaches.
                    float horizon = 0.0;
                    for (int s = 1; s <= steps; s++)
                    {
                        float2 at = v.uv + march * (pixelRadius * float(s) / float(steps)) / screen;
                        if (any(at < 0.0) || any(at > 1.0))
                        {
                            break;
                        }

                        float3 delta = viewPosition(at, sceneDepth.SampleLevel(pointClamp, at, 0).r) - origin;
                        float travelled = length(delta);
                        if (travelled < 1.0e-4)
                        {
                            continue;
                        }

                        // Squared falloff, so something well outside the radius stops counting
                        // rather than casting occlusion across the whole scene.
                        float falloff = max(1.0 - dot(delta, delta) / (radius * radius), 0.0);
                        horizon = max(horizon, (dot(delta / travelled, normal) - 0.02) * falloff);
                    }
                    occlusion += horizon;
                }

                return 1.0 - occlusion / float(directions);
            }
            """);
        _uInverseProjection = _occlusion.Offset("inverseProjection");

        _blur = new ShaderProgram(
            ShaderProgram.FullScreenTriangleVertex,
            $$"""
            {{ShaderProgram.ScreenVaryingsHlsl}}
            Texture2D occlusion : register(t0);
            Texture2D sceneDepth : register(t1);
            cbuffer Params : register(b0)
            {
                float2 direction;
            };

            float main(ScreenVaryings v) : SV_Target
            {
                float2 size;
                sceneDepth.GetDimensions(size.x, size.y);
                float centre = sceneDepth.SampleLevel(pointClamp, v.uv, 0).r;
                float sum = 0.0;
                float weight = 0.0;
                [unroll] for (int i = -3; i <= 3; i++)
                {
                    float2 at = v.uv + direction * float(i) / size;
                    // Bilateral: samples across a depth edge drop out, so the occlusion under a
                    // rock does not bleed onto the ground behind it.
                    float w = exp(-abs(sceneDepth.SampleLevel(pointClamp, at, 0).r - centre) * 8000.0);
                    sum += occlusion.SampleLevel(linearClamp, at, 0).r * w;
                    weight += w;
                }
                return sum / weight;
            }
            """);
        _bDirection = _blur.Offset("direction");
    }

    /// <summary>
    /// Fills the occlusion texture from the scene depth and returns it. Two blur passes, so the
    /// dither pattern the sampling leaves behind averages out along both axes.
    /// </summary>
    public ID3D11ShaderResourceView Render(RenderTargets targets, Matrix4x4 projection)
    {
        using GpuState pass = new();
        Matrix4x4.Invert(projection, out Matrix4x4 inverse);

        GpuState.Depth(DepthMode.Off);
        Context.RSSetViewport(0, 0, targets.Width, targets.Height);

        Presentation surfaces = targets.Presentation!;
        Context.OMSetRenderTargets(surfaces.Occlusion.Target);
        _occlusion.Use();
        _occlusion.Set(_uInverseProjection, inverse);
        Context.PSSetShaderResource(0, targets.Depth.View);
        _occlusion.Draw(3);

        _blur.Use();
        Context.PSSetShaderResource(1, targets.Depth.View);
        Blur(surfaces.OcclusionBlur, surfaces.Occlusion, Vector2.UnitX);
        Blur(surfaces.Occlusion, surfaces.OcclusionBlur, Vector2.UnitY);
        return surfaces.Occlusion.View;
    }

    /// <summary>The source leaves slot 0 before its partner becomes the target, and the target's
    /// own view is never bound while it is drawn into.</summary>
    private void Blur(ColourTarget into, ColourTarget source, Vector2 direction)
    {
        Context.PSSetShaderResource(0, null!);
        Context.OMSetRenderTargets(into.Target);
        _blur.Set(_bDirection, direction);
        Context.PSSetShaderResource(0, source.View);
        _blur.Draw(3);
    }

    public void Dispose()
    {
        _occlusion.Dispose();
        _blur.Dispose();
    }
}
