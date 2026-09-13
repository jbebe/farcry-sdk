// The world at night as the eye sees it, drawn over every sample the world drew and none of the sky.

sampler2D Scene : register(s0);

// x  how far colour drains where it is darkest
// y  how far the drained picture turns from grey to what the rods see
// z  the brightness at and above which a pixel keeps all its colour
// w  the brightness below which it keeps none
float4 Gate : register(c71);

// x   the grain's strength, in the target's own units
// y   which of the grain's patterns shows this moment
// zw  one pixel, in texture coordinates
float4 Grain : register(c72);

static const float3 LUMA = float3(0.299f, 0.587f, 0.114f);

// The rods' response, which peaks in blue-green, so reds go dark and blues hold.
static const float3 ROD = float3(0.05f, 0.55f, 0.40f);

// The blue-grey of rod vision, at a luminance of one so it turns the hue and not the level.
static const float3 ROD_HUE = float3(0.85f, 1.0f, 1.41f);

float4 MainPS(float2 screen : VPOS) : COLOR0
{
    float3 colour = tex2D(Scene, (screen + 0.5f) * Grain.zw).rgb;
    float luminance = dot(colour, LUMA);

    // Fires, lamps and headlights are bright enough for the cones, so they keep their colour.
    float drain = Gate.x * (1.0f - smoothstep(Gate.w, Gate.z, luminance));
    float rods = lerp(luminance, dot(colour, ROD), Gate.y);
    colour = lerp(colour, rods * lerp(1.0f, ROD_HUE, Gate.y), drain);

    float noise = frac(52.9829189f * frac(dot(screen + Grain.y * float2(1.0f, 1.7f),
                                              float2(0.06711056f, 0.00583715f))));
    float dark = 1.0f - smoothstep(0.0f, Gate.z, luminance);
    colour += (noise - 0.5f) * Grain.x * dark;
    return float4(max(colour, 0.0f), 1.0f);
}
