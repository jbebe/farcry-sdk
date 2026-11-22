// The land painted under the horizon where the drawn world ends short of it.

// Over how far below its top, as a sine, the filled land gives way to the sky's own horizon.
#define FILL_SOFTNESS 0.003f

// How bright the world draws land lost wholly in fog, against the fog colour as the sky draws it
// (seen in game, GOG v1.03, a downpour at noon).
#define FOGGED_LAND 0.95f

// The fog's colour where the horizon meets it, at the fogged land's brightness as far as `veil` says
// the weather's fog swallows the land before the horizon.
float3 Veiled(float3 fog, float veil) {
    return fog * lerp(1.0f, FOGGED_LAND, veil);
}

// How much of a unit ray the filled land covers. `fill`: x how much it shows, y the sine of how far
// above the horizon it reaches.
float Filled(float3 ray, float2 fill) {
    return fill.x * saturate((fill.y - ray.z) / FILL_SOFTNESS);
}
