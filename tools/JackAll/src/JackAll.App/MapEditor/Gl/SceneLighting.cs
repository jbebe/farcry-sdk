using JackAll.Tools.World;
using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

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

    /// <summary>The same, for the colours that arrive from the world data rather than from this
    /// file - the descriptor's fog and the materials' tints are all System.Numerics.</summary>
    public static System.Numerics.Vector3 Linear(System.Numerics.Vector3 srgb)
        => new(Linear(srgb.X), Linear(srgb.Y), Linear(srgb.Z));

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

    /// <summary>The current world's authored fog, applied by <c>applyHaze</c> via uniforms; set at
    /// world load and left at the retail default until one is.</summary>
    public static WorldEnvironment Fog { get; set; } = WorldEnvironment.Default;

    /// <summary>Seconds since the viewport started, for anything that moves. The water is the only
    /// caller today; there has been no clock in the renderer since the old ripples came out.</summary>
    public static float Time { get; set; }

    /// <summary>This frame's shadow cascades, or null with the presentation switched off. Written
    /// once per frame and read by every layer, for the same reason <see cref="Exposure"/> is: two
    /// layers holding their own copy is two layers that can disagree about where a shadow falls.
    /// </summary>
    public static ShadowCascades? Shadows { get; set; }

    /// <summary>This frame's occlusion texture, or 0 with the presentation switched off. On the same
    /// terms as <see cref="Shadows"/> - the handle rather than the buffers it came from, so a layer
    /// cannot reach the frame's framebuffers through the lighting.</summary>
    public static int OcclusionMap { get; set; }

    /// <summary>
    /// The presentation switch behind the viewport's Demo checkbox: every pass, every uniform and
    /// every shader branch that exists to make the map look like the game rather than to show what
    /// is in it. Off, the frame is one geometry pass of flatly lit textures.
    /// </summary>
    public static bool Demo { get; set; } = true;

    /// <summary>
    /// The sky as a GLSL function, shared so water reflects exactly the sky that gets drawn rather
    /// than an approximation of it. Covers the gradient and the sun's broad halo; the sharp disc
    /// belongs to <see cref="SkyLayer"/> alone, because a mirrored disc aliases badly on water and a
    /// specular highlight reads better anyway.
    /// </summary>
    /// <remarks>Every program pasting this block needs a <see cref="SkyBinding"/> to fill the
    /// uniforms it declares. Left at their zero defaults the fog is disabled entirely and the
    /// presentation reads as off, which is visible enough not to ship by accident.</remarks>
    public static string SkyGlsl { get; } =
        $$"""
        const vec3 skyZenith  = vec3({{Glsl(Linear(Zenith))}});
        const vec3 skyHorizon = vec3({{Glsl(Linear(Horizon))}});
        const vec3 skyNadir   = vec3({{Glsl(Linear(Nadir))}});
        const vec3 sunTint    = vec3({{Glsl(Linear(SunColour))}});

        // The world's authored fog: (start, 1/(end-start), amount) and its colour, from the
        // descriptor's <Fog> element rather than a guess.
        uniform vec3 fogSetup;
        uniform vec3 fogTint;

        // The presentation switch, declared once here because every surface shader pastes this
        // block. Uniform across the draw, so the branches it guards cost nothing to take.
        uniform float demo;

        vec3 skyColour(vec3 ray, vec3 sunDirection)
        {
            vec3 sky = ray.z >= 0.0
                ? mix(skyHorizon, skyZenith, pow(clamp(ray.z, 0.0, 1.0), 0.42))
                : mix(skyHorizon, skyNadir, clamp(-ray.z * 2.5, 0.0, 1.0));
            return sky + sunTint * pow(max(dot(ray, sunDirection), 0.0), 8.0) * 0.30;
        }

        // The authored linear fog, with one editor liberty kept: dust sits in the low air, so the
        // haze thins with altitude and peaks rise out of it. Every surface in the scene has to run
        // this same function or it will visibly float out of the haze the rest of the world sits in.
        vec3 applyHaze(vec3 colour, float viewDistance, float height)
        {
            float fog = clamp((viewDistance - fogSetup.x) * fogSetup.y, 0.0, 1.0) * fogSetup.z;
            fog *= exp(-max(height - 30.0, 0.0) / 140.0);
            return mix(colour, fogTint, clamp(fog, 0.0, 1.0));
        }
        """;

    /// <summary>
    /// Both directions of the sRGB transfer function. The decode and the encode are exact inverses
    /// over the same four constants, and they are the two ends of one pipeline - kept together so
    /// neither can be adjusted without the other.
    /// </summary>
    public static string ColourGlsl { get; } =
        """
        // For albedo whose upload cannot be tagged sRGB: the model texture cache is keyed by path
        // and serves the diffuse, the second diffuse and the blend mask alike, and only the first
        // two are colours. The terrain, whose roles are separable, uses the sRGB formats instead.
        vec3 srgbToLinear(vec3 c)
        {
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
        }

        vec3 linearToSrgb(vec3 c)
        {
            return mix(c * 12.92, 1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, step(vec3(0.0031308), c));
        }
        """;

    /// <summary>
    /// The cascade lookup, pasted by every program whose surfaces take sun. Kept beside the rest of
    /// the lighting for the same reason everything else here is: a second copy of the cascade choice
    /// is a second chance for the terrain and the models to disagree about where a shadow falls.
    /// </summary>
    /// <remarks>A program pasting this needs a <see cref="ShadowBinding"/> to fill it; left unset,
    /// <c>shadowStrength</c> is zero and the lookup returns full sun.</remarks>
    public static string ShadowGlsl { get; } =
        $$"""
        uniform sampler2DArrayShadow shadowMap;
        uniform mat4 shadowMatrices[{{ShadowCascades.Count}}];
        uniform vec4 shadowSplits;
        uniform float shadowStrength;

        // What the real-time shadow is still worth at this distance. It reaches nothing at the last
        // cascade, which is exactly where the terrain's baked lightmap has to take back over.
        float shadowFade(float viewDistance)
        {
            return 1.0 - smoothstep(shadowSplits.w * 0.75, shadowSplits.w, viewDistance);
        }

        float sampleShadow(vec3 worldPosition, float viewDistance, float ndotl)
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

            vec4 clip = shadowMatrices[cascade] * vec4(worldPosition, 1.0);
            // Orthographic, so w is exactly 1 and the perspective divide would be three no-ops.
            vec3 uv = clip.xyz * 0.5 + 0.5;
            if (uv.z > 1.0)
            {
                return 1.0;
            }

            // Slope-scaled rather than normal-offset: a facet the sun only grazes needs far more
            // bias than one facing it, and neither the eye-flipped mesh normals nor the terrain's
            // finite-difference ones are trustworthy enough to push a sample along.
            // Constant plus slope-scaled, in light-clip depth. All of the bias is here: the terrain
            // sets and clears PolygonOffsetFill inside its own draw, so an outer one would not survive.
            float bias = 0.0006 + 0.0030 * (1.0 - ndotl);
            float texel = 1.0 / {{ShadowCascades.Size}}.0;

            float sum = 0.0;
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    sum += texture(shadowMap,
                        vec4(uv.xy + vec2(x, y) * texel, float(cascade), uv.z - bias));
                }
            }
            return sum / 9.0;
        }
        """;
    /// <summary>
    /// The lighting every solid surface runs - terrain, entity meshes, the scatter. Shaped after the
    /// engine's own terrain pixel shader (shadersobj/engine/shaders/obj10, DXBC with its RDEF names
    /// intact), which differs from a flat ambient in three ways that are all visible: the ambient is
    /// directional and coloured, the sun ADDS to it before the albedo multiplies, and specular is a
    /// separate additive Blinn term rather than something folded into the diffuse.
    /// </summary>
    public static string SurfaceGlsl { get; } =
        $$"""
        {{ColourGlsl}}

        uniform sampler2D occlusionMap;
        uniform float occlusionStrength;

        // What the screen-space pass found for this fragment. It reaches the ambient term and
        // nothing else: a face the sun is on does not go darker for standing near a wall.
        float ambientOcclusion()
        {
            if (occlusionStrength <= 0.0)
            {
                return 1.0;
            }
            return texture(occlusionMap, gl_FragCoord.xy / vec2(textureSize(occlusionMap, 0))).r;
        }

        const vec3 sunLight      = vec3({{Glsl(SunLight)}});
        const vec3 ambientSky    = vec3({{Glsl(AmbientSky)}});
        const vec3 ambientGround = vec3({{Glsl(AmbientGround)}});
        const float specularStrength = {{Glsl(SpecularStrength)}};

        // Directional ambient plus sun. With the presentation off this is the whole of the shading -
        // called with no occlusion, and with none of the cascade lookup, highlight or haze that
        // shadeSurface goes on to add. One expression for both, so switching the presentation
        // changes what the frame computes rather than how bright the world is.
        vec3 shadeDiffuse(vec3 albedo, vec3 normal, float sunAmount, float occlusion)
        {
            vec3 ambient = mix(ambientGround, ambientSky, normal.z * 0.5 + 0.5) * occlusion;
            return albedo * (ambient + sunLight * sunAmount);
        }

        // sunAmount is how much sun reaches this fragment with N.L already folded in, because the
        // callers answer that differently: a mesh takes max(dot(n, L), 0), the terrain multiplies
        // that by its baked shadow. Everything downstream - including whether a highlight is
        // allowed at all - is identical for both, which is the point of this living in one place.
        // Keep this source ASCII: a stray non-ASCII byte, even in a comment, stops the GLSL
        // tokeniser dead with an unexpected end of file.
        vec3 shadeSurface(
            vec3 albedo, vec3 normal, vec3 toEye, vec3 sunDirection, float sunAmount,
            vec3 specularColour, float specularPower)
        {
            vec3 lit = shadeDiffuse(albedo, normal, sunAmount, ambientOcclusion());
            if (specularPower > 0.0 && sunAmount > 0.0)
            {
                // Blinn, gated on sunAmount so a facet the sun does not reach cannot catch a
                // highlight. The colour clamp is render-side on purpose: the .xbm values stay as
                // authored, and retail runs to 2.0 because the engine multiplies a spec map into
                // them that this renderer does not have.
                vec3 sum = sunDirection + toEye;
                float len = length(sum);
                if (len > 1.0e-4)
                {
                    float blinn = pow(max(dot(normal, sum / len), 0.0), specularPower);
                    lit += min(specularColour, vec3(1.5)) * sunLight
                         * (sunAmount * blinn * specularStrength);
                }
            }
            return lit;
        }
        """;

    private static string Glsl(Vector3 v) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture, $"{v.X:0.####}, {v.Y:0.####}, {v.Z:0.####}");

    /// <summary>Invariant, so a comma-decimal locale cannot turn one constant into two.</summary>
    private static string Glsl(float v)
        => v.ToString("0.0####", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// The uniforms <see cref="SceneLighting.SkyGlsl"/> declares, resolved on one program. Same shape as
/// <see cref="ShadowBinding"/> and <see cref="OcclusionBinding"/>: the block is shared GLSL, so what
/// fills it has to be shared too rather than hand-carried as a list of locations per layer.
/// </summary>
public sealed class SkyBinding
{
    private readonly int _demo;
    private readonly int _fogSetup;
    private readonly int _fogTint;

    public SkyBinding(ShaderProgram program)
    {
        _demo = program.UniformLocation("demo");
        _fogSetup = program.UniformLocation("fogSetup");
        _fogTint = program.UniformLocation("fogTint");
    }

    /// <summary>Uploads the switch and the current world's fog to whichever program is in use.
    /// </summary>
    public void Apply()
    {
        OpenTK.Graphics.OpenGL4.GL.Uniform1(_demo, SceneLighting.Demo ? 1f : 0f);

        WorldEnvironment fog = SceneLighting.Fog;
        float range = MathF.Max(fog.FogEnd - fog.FogStart, 1f);
        OpenTK.Graphics.OpenGL4.GL.Uniform3(_fogSetup, fog.FogStart, 1f / range, fog.FogAmount);

        // The descriptor writes fog as an 8-bit triple - an authored sRGB colour, like every other
        // colour in this file, and the haze mixes toward it in linear radiance.
        System.Numerics.Vector3 tint = SceneLighting.Linear(fog.FogColour);
        OpenTK.Graphics.OpenGL4.GL.Uniform3(_fogTint, tint.X, tint.Y, tint.Z);
    }
}
