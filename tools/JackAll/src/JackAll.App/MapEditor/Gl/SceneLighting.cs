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
    public static readonly Vector3 SunLight = SunColour * 1.15f;

    /// <summary>Sky bounce for the up-facing half of the ambient hemisphere. Derived from the sky
    /// the scene actually draws rather than picked, so the ambient cannot drift from the sky.</summary>
    public static readonly Vector3 AmbientSky = Vector3.Lerp(Zenith, Horizon, 0.45f) * 0.62f;

    /// <summary>Ground bounce for the down-facing half, from the same nadir the sky uses.</summary>
    public static readonly Vector3 AmbientGround = Nadir * 0.80f;

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
    public static float Exposure { get; set; } = 1.2f;

    /// <summary>The current world's authored fog, applied by <c>applyHaze</c> via uniforms; set at
    /// world load and left at the retail default until one is.</summary>
    public static WorldEnvironment Fog { get; set; } = WorldEnvironment.Default;

    /// <summary>
    /// The sky as a GLSL function, shared so water reflects exactly the sky that gets drawn rather
    /// than an approximation of it. Covers the gradient and the sun's broad halo; the sharp disc
    /// belongs to <see cref="SkyLayer"/> alone, because a mirrored disc aliases badly on water and a
    /// specular highlight reads better anyway.
    /// </summary>
    /// <remarks>Every program pasting this block that also calls <c>applyHaze</c> must set the two
    /// fog uniforms - see <see cref="SetFogUniforms"/>. Left at their zero defaults they disable the
    /// fog entirely, which is visible enough not to ship by accident.</remarks>
    public static string SkyGlsl { get; } =
        $$"""
        const vec3 skyZenith  = vec3({{Glsl(Zenith)}});
        const vec3 skyHorizon = vec3({{Glsl(Horizon)}});
        const vec3 skyNadir   = vec3({{Glsl(Nadir)}});
        const vec3 sunTint    = vec3({{Glsl(SunColour)}});

        // The world's authored fog: (start, 1/(end-start), amount) and its colour, from the
        // descriptor's <Fog> element rather than a guess.
        uniform vec3 fogSetup;
        uniform vec3 fogTint;

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
        vec3 applyHaze(vec3 colour, float viewDistance, float height, float haze)
        {
            float fog = clamp((viewDistance - fogSetup.x) * fogSetup.y, 0.0, 1.0) * fogSetup.z;
            fog *= exp(-max(height - 30.0, 0.0) / 140.0);
            return mix(colour, fogTint, clamp(fog * haze, 0.0, 1.0));
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
        const vec3 sunLight      = vec3({{Glsl(SunLight)}});
        const vec3 ambientSky    = vec3({{Glsl(AmbientSky)}});
        const vec3 ambientGround = vec3({{Glsl(AmbientGround)}});
        const float specularStrength = {{Glsl(SpecularStrength)}};

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
            vec3 ambient = mix(ambientGround, ambientSky, normal.z * 0.5 + 0.5);
            vec3 lit = albedo * (ambient + sunLight * sunAmount);
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

    /// <summary>Sets the two fog uniforms <see cref="SkyGlsl"/> declares, from the current world's
    /// authored values. Callers pass locations they resolved on their own program.</summary>
    public static void SetFogUniforms(int fogSetupLocation, int fogTintLocation)
    {
        WorldEnvironment fog = Fog;
        float range = MathF.Max(fog.FogEnd - fog.FogStart, 1f);
        OpenTK.Graphics.OpenGL4.GL.Uniform3(
            fogSetupLocation, fog.FogStart, 1f / range, fog.FogAmount);
        OpenTK.Graphics.OpenGL4.GL.Uniform3(
            fogTintLocation, fog.FogColour.X, fog.FogColour.Y, fog.FogColour.Z);
    }

    private static string Glsl(Vector3 v) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture, $"{v.X:0.####}, {v.Y:0.####}, {v.Z:0.####}");

    /// <summary>Invariant, so a comma-decimal locale cannot turn one constant into two.</summary>
    private static string Glsl(float v)
        => v.ToString("0.0####", System.Globalization.CultureInfo.InvariantCulture);
}
