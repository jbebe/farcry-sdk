// A tracer's streak in place of the engine's textured quad: a glow with a hotter core, dark at every
// edge and sampling nothing, brighter than white so that it blooms. The engine's primitive vertex
// shaders hand on the vertex colour in TEXCOORD0, and in TEXCOORD1 U along, V across. A NATO round's
// streak is orange-red; a Soviet round's, built with its vertex alpha lowered, green. The weaker
// channels stay below one even where glow, core and tip add up, so the core keeps the round's colour.

static const float3 kGlow = float3(1.6, 0.24, 0.02);
static const float3 kCore = float3(2.4, 0.16, 0.02);
static const float3 kSovietGlow = float3(0.16, 1.6, 0.1);
static const float3 kSovietCore = float3(0.12, 2.4, 0.08);

float4 TracerPS(float4 colour : TEXCOORD0, float2 uv : TEXCOORD1) : COLOR {
    const bool soviet = colour.a < 0.75;
    const float3 glowTint = soviet ? kSovietGlow : kGlow;
    const float3 coreTint = soviet ? kSovietCore : kCore;
    const float across = uv.y * 2.0 - 1.0;
    const float glow = exp(-across * across / 0.2);
    const float core = exp(-across * across / 0.03);
    const float front = smoothstep(1.0, 0.9, uv.x);
    const float along = smoothstep(0.0, 0.5, uv.x) * front;
    const float tip = exp(-pow((uv.x - 0.88) / 0.05, 2.0)) * front;
    const float3 light = glowTint * along * glow + coreTint * (along + tip) * core;
    return float4(light * colour.rgb, 1.0);
}
