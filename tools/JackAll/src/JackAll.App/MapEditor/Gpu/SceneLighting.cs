using System.Numerics;
using System.Runtime.InteropServices;
using JackAll.Tools.World;
using Vortice.Direct3D11;
using static JackAll.App.MapEditor.Gpu.GpuDevice;

namespace JackAll.App.MapEditor.Gpu;

/// <summary>
/// What the sky, the surface shading and the fog all have to agree on. Kept in one place because the
/// last lighting bug was exactly a disagreement: terrain was lit from the opposite side to the baked
/// map, and nothing in the code said the two were meant to match.
/// </summary>
public static class SceneLighting
{
    /// <summary>Direction to the sun. West, matching the direction fitted from the baked terrain
    /// lightmap, but well above that fit's 10 degrees so flat ground still catches light.</summary>
    public static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(-0.72f, 0f, 0.70f));

    public static readonly Vector3 SunColour = new(1.00f, 0.93f, 0.78f);

    public static readonly Vector3 Zenith = new(0.17f, 0.40f, 0.74f);

    /// <summary>Horizon haze colour for the sky gradient. The fog is its own colour now - the
    /// authored one from the world descriptor - so this only paints the sky.</summary>
    public static readonly Vector3 Horizon = new(0.80f, 0.75f, 0.62f);

    /// <summary>Ground colour below the horizon, for looking down from altitude.</summary>
    public static readonly Vector3 Nadir = new(0.28f, 0.24f, 0.19f);

    /// <summary>Sun radiance rather than sun tint: what actually lands on a surface, against
    /// <see cref="SunColour"/> which is what the sky and the water glint show.</summary>
    public static readonly Vector3 SunLight = Linear(SunColour) * 1.15f;

    /// <summary>Sky bounce for the up-facing half of the ambient hemisphere. Derived from the sky
    /// the scene actually draws rather than picked, so the ambient cannot drift from the sky.</summary>
    public static readonly Vector3 AmbientSky =
        Vector3.Lerp(Linear(Zenith), Linear(Horizon), 0.45f) * 0.62f;

    /// <summary>Ground bounce for the down-facing half, from the same nadir the sky uses.</summary>
    public static readonly Vector3 AmbientGround = Linear(Nadir) * 0.80f;

    /// <summary>
    /// The sRGB transfer function, applied to every authored colour on its way into the shading.
    /// The constants above were picked by eye against a display, so they are sRGB values; the
    /// shading now runs on linear radiance, and feeding one to the other is the difference between
    /// an ambient that looks right and one that is roughly twice as bright as intended.
    /// </summary>
    public static Vector3 Linear(Vector3 srgb) => new(Linear(srgb.X), Linear(srgb.Y), Linear(srgb.Z));

    private static float Linear(float c)
        => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    /// <summary>
    /// Tames the retail specular data, which is authored for an engine that also multiplies a spec
    /// map into it (1,743 of 2,208 materials name a SpecularTexture1 this renderer does not read).
    /// Exponents run 2-20 and colours to 2.0, so applied literally a highlight covers half a surface.
    /// </summary>
    public const float SpecularStrength = 0.35f;

    /// <summary>
    /// Scene exposure, written once per frame from the viewport's slider and read by every layer
    /// that lights geometry. A static for the same reason <see cref="SunDirection"/> is: a layer
    /// handed its own copy is a layer that can quietly disagree, and models being 1.5x darker than
    /// the ground was exactly that bug.
    /// </summary>
    public static float Exposure { get; set; } = 1.8f;

    /// <summary>The current world's authored fog, applied by <c>applyHaze</c> through the frame's
    /// constants; set at world load and left at the retail default until one is.</summary>
    public static WorldEnvironment Fog { get; set; } = WorldEnvironment.Default;

    /// <summary>Seconds since the viewport started, for anything that moves. The water is the only
    /// caller today; there has been no clock in the renderer since the old ripples came out.</summary>
    public static float Time { get; set; }

    /// <summary>This frame's shadow cascades, or null with the presentation switched off. Written
    /// once per frame and read by every layer, for the same reason <see cref="Exposure"/> is: two
    /// layers holding their own copy is two layers that can disagree about where a shadow falls.
    /// </summary>
    public static ShadowCascades? Shadows { get; set; }

    /// <summary>This frame's occlusion, or null with the presentation switched off. On the same
    /// terms as <see cref="Shadows"/> - the texture rather than the targets it came from, so a layer
    /// cannot reach the frame's targets through the lighting.</summary>
    public static ID3D11ShaderResourceView? OcclusionMap { get; set; }

    /// <summary>
    /// The presentation switch behind the viewport's Demo checkbox: every pass, every constant and
    /// every shader branch that exists to make the map look like the game rather than to show what
    /// is in it. Off, the frame is one geometry pass of flatly lit textures.
    /// </summary>
    public static bool Demo { get; set; }

    /// <summary>
    /// The sky as an HLSL function, shared so water reflects exactly the sky that gets drawn rather
    /// than an approximation of it. Covers the gradient and the sun's broad halo; the sharp disc
    /// belongs to <see cref="SkyLayer"/> alone, because a mirrored disc aliases badly on water and a
    /// specular highlight reads better anyway. The fog and the switch come from the frame's constants.
    /// </summary>
    public static string SkyHlsl { get; } =
        $$"""
        static const float3 skyZenith  = float3({{Hlsl(Linear(Zenith))}});
        static const float3 skyHorizon = float3({{Hlsl(Linear(Horizon))}});
        static const float3 skyNadir   = float3({{Hlsl(Linear(Nadir))}});
        static const float3 sunTint    = float3({{Hlsl(Linear(SunColour))}});
        static const float3 sunDirection = float3({{Hlsl(SunDirection)}});

        float3 skyColour(float3 ray, float3 sunDirection)
        {
            float3 sky = ray.z >= 0.0
                ? lerp(skyHorizon, skyZenith, pow(saturate(ray.z), 0.42))
                : lerp(skyHorizon, skyNadir, saturate(-ray.z * 2.5));
            return sky + sunTint * pow(max(dot(ray, sunDirection), 0.0), 8.0) * 0.30;
        }

        // The authored linear fog, with one editor liberty kept: dust sits in the low air, so the
        // haze thins with altitude and peaks rise out of it. Every surface in the scene has to run
        // this same function or it will visibly float out of the haze the rest of the world sits in.
        float3 applyHaze(float3 colour, float viewDistance, float height)
        {
            float fog = saturate((viewDistance - fogSetup.x) * fogSetup.y) * fogSetup.z;
            fog *= exp(-max(height - 30.0, 0.0) / 140.0);
            return lerp(colour, fogTint, saturate(fog));
        }
        """;

    /// <summary>
    /// Both directions of the sRGB transfer function. The decode and the encode are exact inverses
    /// over the same four constants, and they are the two ends of one pipeline - kept together so
    /// neither can be adjusted without the other.
    /// </summary>
    public static string ColourHlsl { get; } =
        """
        // For albedo whose upload cannot be tagged sRGB: the model texture cache is keyed by path
        // and serves the diffuse, the second diffuse and the blend mask alike, and only the first
        // two are colours. The terrain, whose roles are separable, uses the sRGB formats instead.
        float3 srgbToLinear(float3 c)
        {
            return lerp(c / 12.92, pow(abs((c + 0.055) / 1.055), 2.4), step(0.04045, c));
        }

        float3 linearToSrgb(float3 c)
        {
            return lerp(c * 12.92, 1.055 * pow(abs(c), 1.0 / 2.4) - 0.055, step(0.0031308, c));
        }
        """;

    /// <summary>
    /// The cascade lookup, pasted by every program whose surfaces take sun. Kept beside the rest of
    /// the lighting for the same reason everything else here is: a second copy of the cascade choice
    /// is a second chance for the terrain and the models to disagree about where a shadow falls.
    /// </summary>
    /// <remarks>With no cascades this frame, <c>shadowStrength</c> is zero and the lookup returns full
    /// sun.</remarks>
    public static string ShadowHlsl { get; } =
        $$"""
        Texture2D shadowMap0 : register(t{{GpuState.ShadowSlot}});
        Texture2D shadowMap1 : register(t{{GpuState.ShadowSlot + 1}});
        Texture2D shadowMap2 : register(t{{GpuState.ShadowSlot + 2}});
        Texture2D shadowMap3 : register(t{{GpuState.ShadowSlot + 3}});

        // 3x3 compares around one texel of one cascade.
        float shadowTaps(Texture2D map, float3 uv, float bias)
        {
            float texel = 1.0 / {{ShadowCascades.Size}}.0;
            float sum = 0.0;
            [unroll] for (int y = -1; y <= 1; y++)
            {
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    sum += map.SampleCmpLevelZero(shadowCompare, uv.xy + float2(x, y) * texel, uv.z - bias);
                }
            }
            return sum / 9.0;
        }

        // What the real-time shadow is still worth at this distance. It reaches nothing at the last
        // cascade, which is exactly where the terrain's baked lightmap has to take back over.
        float shadowFade(float viewDistance)
        {
            return 1.0 - smoothstep(shadowSplits.w * 0.75, shadowSplits.w, viewDistance);
        }

        float sampleShadow(float3 worldPosition, float viewDistance, float ndotl)
        {
            // Both callers multiply the result by ndotl, so on a face turned away from the sun the
            // nine compares below would be computed and then multiplied by zero.
            if (shadowStrength <= 0.0 || ndotl <= 0.0 || viewDistance >= shadowSplits.w)
            {
                return 1.0;
            }

            int cascade = viewDistance < shadowSplits.x ? 0
                        : viewDistance < shadowSplits.y ? 1
                        : viewDistance < shadowSplits.z ? 2 : 3;

            float4 clip = mul(shadowMatrices[cascade], float4(worldPosition, 1.0));
            // Orthographic, so w is exactly 1, and its depth already runs 0 to 1.
            float3 uv = float3(clip.xy * 0.5 + 0.5, clip.z);
            if (uv.z > 1.0)
            {
                return 1.0;
            }

            // Slope-scaled rather than normal-offset: a facet the sun only grazes needs far more
            // bias than one facing it, and neither the eye-flipped mesh normals nor the terrain's
            // finite-difference ones are trustworthy enough to push a sample along.
            // Constant plus slope-scaled, in light-clip depth. All of the bias is here: the terrain
            // sets and clears its own depth bias inside its draw, so an outer one would not survive.
            float bias = 0.0006 + 0.0030 * (1.0 - ndotl);
            [branch] if (cascade == 0) { return shadowTaps(shadowMap0, uv, bias); }
            [branch] if (cascade == 1) { return shadowTaps(shadowMap1, uv, bias); }
            [branch] if (cascade == 2) { return shadowTaps(shadowMap2, uv, bias); }
            return shadowTaps(shadowMap3, uv, bias);
        }
        """;

    /// <summary>
    /// The lighting every solid surface runs - terrain, entity meshes, the scatter. Shaped after the
    /// engine's own terrain pixel shader (shadersobj/engine/shaders/obj10, DXBC with its RDEF names
    /// intact), which differs from a flat ambient in three ways that are all visible: the ambient is
    /// directional and coloured, the sun ADDS to it before the albedo multiplies, and specular is a
    /// separate additive Blinn term rather than something folded into the diffuse.
    /// </summary>
    public static string SurfaceHlsl { get; } =
        $$"""
        {{ColourHlsl}}

        Texture2D occlusionMap : register(t{{GpuState.OcclusionSlot}});

        // What the screen-space pass found for this pixel. It reaches the ambient term and nothing
        // else: a face the sun is on does not go darker for standing near a wall.
        float ambientOcclusion(float2 pixel)
        {
            if (occlusionStrength <= 0.0)
            {
                return 1.0;
            }
            return occlusionMap.Load(int3(pixel, 0)).r;
        }

        static const float3 sunLight      = float3({{Hlsl(SunLight)}});
        static const float3 ambientSky    = float3({{Hlsl(AmbientSky)}});
        static const float3 ambientGround = float3({{Hlsl(AmbientGround)}});
        static const float specularStrength = {{Hlsl(SpecularStrength)}};

        // Directional ambient plus sun. With the presentation off this is the whole of the shading -
        // called with no occlusion, and with none of the cascade lookup, highlight or haze that
        // shadeSurface goes on to add. One expression for both, so switching the presentation
        // changes what the frame computes rather than how bright the world is.
        float3 shadeDiffuse(float3 albedo, float3 normal, float sunAmount, float occlusion)
        {
            float3 ambient = lerp(ambientGround, ambientSky, normal.z * 0.5 + 0.5) * occlusion;
            return albedo * (ambient + sunLight * sunAmount);
        }

        // sunAmount is how much sun reaches this fragment with N.L already folded in, because the
        // callers answer that differently: a mesh takes max(dot(n, L), 0), the terrain multiplies
        // that by its baked shadow. Everything downstream - including whether a highlight is
        // allowed at all - is identical for both, which is the point of this living in one place.
        float3 shadeSurface(
            float3 albedo, float3 normal, float3 toEye, float3 sunDirection, float sunAmount,
            float3 specularColour, float specularPower, float2 pixel)
        {
            float3 lit = shadeDiffuse(albedo, normal, sunAmount, ambientOcclusion(pixel));
            if (specularPower > 0.0 && sunAmount > 0.0)
            {
                // Blinn, gated on sunAmount so a facet the sun does not reach cannot catch a
                // highlight. The colour clamp is render-side on purpose: the .xbm values stay as
                // authored, and retail runs to 2.0 because the engine multiplies a spec map into
                // them that this renderer does not have.
                float3 sum = sunDirection + toEye;
                float len = length(sum);
                if (len > 1.0e-4)
                {
                    float blinn = pow(max(dot(normal, sum / len), 0.0), specularPower);
                    lit += min(specularColour, 1.5) * sunLight
                         * (sunAmount * blinn * specularStrength);
                }
            }
            return lit;
        }
        """;

    private static string Hlsl(Vector3 v)
        => $"{ShaderProgram.Literal(v.X)}, {ShaderProgram.Literal(v.Y)}, {ShaderProgram.Literal(v.Z)}";

    private static string Hlsl(float v) => ShaderProgram.Literal(v);
}

/// <summary>
/// The <c>Frame</c> constant buffer every program declares (<see cref="ShaderProgram"/>'s prelude),
/// with the cascades and the occlusion on their shared slots. Filled from <see cref="SceneLighting"/>
/// in one place, so no layer can carry its own copy of the fog or the shadow matrices.
/// </summary>
public static class FrameConstants
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Layout
    {
        public Matrix4x4 Shadow0, Shadow1, Shadow2, Shadow3;
        public Vector4 ShadowSplits;
        public Vector3 FogSetup;
        public float ShadowStrength;
        public Vector3 FogTint;
        public float OcclusionStrength;
        public Vector2 ViewportSize;
        public float Demo;
        public float Pad;
    }

    private static ID3D11Buffer? _buffer;

    public static void Release()
    {
        _buffer?.Dispose();
        _buffer = null;
    }

    /// <summary>Uploads the frame's lighting and binds it, with the cascades and the occlusion that
    /// go with it; run again once the presentation's passes have made them.</summary>
    public static void Upload(int width, int height)
    {
        _buffer ??= Device.CreateBuffer((uint)Marshal.SizeOf<Layout>(), BindFlags.ConstantBuffer,
            ResourceUsage.Dynamic, CpuAccessFlags.Write);

        WorldEnvironment fog = SceneLighting.Fog;
        float range = MathF.Max(fog.FogEnd - fog.FogStart, 1f);
        var frame = new Layout
        {
            FogSetup = new Vector3(fog.FogStart, 1f / range, fog.FogAmount),
            // The descriptor writes fog as an 8-bit triple - an authored sRGB colour, like every
            // other colour in SceneLighting, and the haze mixes toward it in linear radiance.
            FogTint = SceneLighting.Linear(fog.FogColour),
            ViewportSize = new Vector2(width, height),
            Demo = SceneLighting.Demo ? 1f : 0f,
            OcclusionStrength = SceneLighting.OcclusionMap is null ? 0f : 1f,
        };
        if (SceneLighting.Shadows is { } cascades)
        {
            frame.ShadowStrength = 1f;
            frame.ShadowSplits = ShadowCascades.Splits;
            frame.Shadow0 = cascades.Matrices[0];
            frame.Shadow1 = cascades.Matrices[1];
            frame.Shadow2 = cascades.Matrices[2];
            frame.Shadow3 = cascades.Matrices[3];
        }

        MappedSubresource mapped = Context.Map(_buffer, MapMode.WriteDiscard);
        Marshal.StructureToPtr(frame, mapped.DataPointer, false);
        Context.Unmap(_buffer);
        Context.VSSetConstantBuffer(1, _buffer);
        Context.PSSetConstantBuffer(1, _buffer);
        Context.PSSetShaderResources(GpuState.ShadowSlot, ShadowCascades.Count,
            SceneLighting.Shadows?.Views ?? GpuState.NoTextures);
        Context.PSSetShaderResource(GpuState.OcclusionSlot, SceneLighting.OcclusionMap!);
    }
}
