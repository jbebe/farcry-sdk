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
}
