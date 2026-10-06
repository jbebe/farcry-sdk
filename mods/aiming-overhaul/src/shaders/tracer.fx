// A tracer's streak, drawn in place of the engine's textured quad: an orange glow across it with a
// hotter core, fading in from its rear and out just short of its front. Every edge of the quad is
// dark, so no edge shows however the streak is turned, and nothing is sampled, so nothing is lost
// to a smaller mip. Brighter than white, so that it blooms.
//
// The engine's world primitive vertex shader hands on the vertex colour in TEXCOORD0 and the
// texture coordinate in TEXCOORD1: U along the streak from its rear, V across it.

static const float3 kGlow = float3(2.0, 0.84, 0.1);
static const float3 kCore = float3(4.0, 2.4, 0.8);

float4 TracerPS(float4 colour : TEXCOORD0, float2 uv : TEXCOORD1) : COLOR {
    const float across = uv.y * 2.0 - 1.0;
    const float glow = exp(-across * across / 0.2);
    const float core = exp(-across * across / 0.03);
    const float front = smoothstep(1.0, 0.9, uv.x);
    const float along = smoothstep(0.0, 0.5, uv.x) * front;
    const float tip = exp(-pow((uv.x - 0.88) / 0.05, 2.0)) * front;
    const float3 light = along * (kGlow * glow + kCore * core) + tip * core * 2.0;
    return float4(light * colour.rgb, 1.0);
}
