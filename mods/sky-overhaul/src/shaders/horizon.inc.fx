// The land painted under the horizon where the drawn world ends short of it.

// Over how far below its top, as a sine, the filled land gives way to the sky's own horizon.
#define FILL_SOFTNESS 0.003f

// How much of a unit ray the filled land covers. `fill`: x how much it shows, y the sine of how far
// above the horizon it reaches.
float Filled(float3 ray, float2 fill) {
    return fill.x * saturate((fill.y - ray.z) / FILL_SOFTNESS);
}
