using OpenTK.Mathematics;

namespace JackAll.App.MapEditor.Gl;

/// <summary>
/// What the sky, the terrain shading and the fog all have to agree on. Kept in one place because the
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

    /// <summary>Horizon haze. The terrain fog fades to this too, so the ground meets the sky
    /// instead of ending at a visible line.</summary>
    public static readonly Vector3 Horizon = new(0.80f, 0.75f, 0.62f);

    /// <summary>Ground colour below the horizon, for looking down from altitude.</summary>
    public static readonly Vector3 Nadir = new(0.28f, 0.24f, 0.19f);

    /// <summary>
    /// The sky as a GLSL function, shared so water reflects exactly the sky that gets drawn rather
    /// than an approximation of it. Covers the gradient and the sun's broad halo; the sharp disc
    /// belongs to <see cref="SkyLayer"/> alone, because a mirrored disc aliases badly on water and a
    /// specular highlight reads better anyway.
    /// </summary>
    public static string SkyGlsl { get; } =
        $$"""
        const vec3 skyZenith  = vec3({{Glsl(Zenith)}});
        const vec3 skyHorizon = vec3({{Glsl(Horizon)}});
        const vec3 skyNadir   = vec3({{Glsl(Nadir)}});
        const vec3 sunTint    = vec3({{Glsl(SunColour)}});

        vec3 skyColour(vec3 ray, vec3 sunDirection)
        {
            vec3 sky = ray.z >= 0.0
                ? mix(skyHorizon, skyZenith, pow(clamp(ray.z, 0.0, 1.0), 0.42))
                : mix(skyHorizon, skyNadir, clamp(-ray.z * 2.5, 0.0, 1.0));
            return sky + sunTint * pow(max(dot(ray, sunDirection), 0.0), 8.0) * 0.30;
        }
        """;

    private static string Glsl(Vector3 v) => string.Create(
        System.Globalization.CultureInfo.InvariantCulture, $"{v.X:0.####}, {v.Y:0.####}, {v.Z:0.####}");
}
