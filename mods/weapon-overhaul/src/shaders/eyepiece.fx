// The eyepiece's screen passes: the gun's depth copied where it can be read back, and the scope's
// body and the reticle laid over the finished frame, black and soft.

// x, y: one pixel across and down, in texture coordinates. z: the reticle's softness, a radius in
// pixels. w: the screen's width over its height.
float4 Soften : register(c0);
// x: how much the softened coverage is strengthened. y: the smallest radius taken for a lens, z: the
// radius the reticle shows within, both in screen heights.
float4 Ink : register(c1);
// The scope's body, in screen heights: x its opening's radius as drawn, y its outer edge's, z the
// mount's half-width, w its softness.
float4 Body : register(c2);

// The reticle's mask, nought where it was drawn; or the housing's, softened.
sampler2D Mask : register(s0);
// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);
// The opening as ScopeLens found it: x its radius, yz its centre off the screen's, y down,
// all in screen heights.
sampler2D Lens : register(s5);

// A disc of taps around the pixel, at half and whole radius.
static const float2 kDisc[12] = {
    float2(1.0f, 0.0f),     float2(0.5f, 0.866f),   float2(-0.5f, 0.866f),
    float2(-1.0f, 0.0f),    float2(-0.5f, -0.866f), float2(0.5f, -0.866f),
    float2(0.433f, 0.25f),  float2(0.0f, 0.5f),     float2(-0.433f, 0.25f),
    float2(-0.433f, -0.25f), float2(0.0f, -0.5f),   float2(0.433f, -0.25f),
};

// Into a float target, which unlike the depth texture can be read back.
float4 DepthPS(float2 uv : TEXCOORD0) : COLOR0 {
    return tex2D(WeaponDepth, uv).r;
}

// Black, as much as the softened housing covers the pixel.
float4 HousingPS(float2 uv : TEXCOORD0) : COLOR0 {
    float ink = 1.0f - tex2D(Mask, uv).r;
    clip(ink - 0.002f);
    return float4(0.0f, 0.0f, 0.0f, ink);
}

// Black over the scope's body, around the lens's centre: a ring around the opening, and a mount from
// its centre down past the screen's foot, each with a soft edge.
float4 BodyPS(float2 uv : TEXCOORD0) : COLOR0 {
    float3 lens = tex2D(Lens, float2(0.5f, 0.5f)).xyz;
    float2 at = (uv - 0.5f) * float2(Soften.w, 1.0f) - lens.yz;
    float distance = length(at);
    float opening = distance - Body.x;
    float outside = distance - Body.y;
    float mount = max(abs(at.x) - Body.z, -at.y);
    float ink = smoothstep(-Body.w, Body.w, opening) *
                (1.0f - smoothstep(-Body.w, Body.w, min(outside, mount)));
    clip(ink - 0.002f);
    return float4(0.0f, 0.0f, 0.0f, ink);
}

// Black, as much as the softened reticle covers the pixel, within the scope's body; the body and
// the housing darken what of it reaches past the opening anyway.
float4 ReticlePS(float2 uv : TEXCOORD0) : COLOR0 {
    float3 lens = tex2D(Lens, float2(0.5f, 0.5f)).xyz;
    float2 at = (uv - 0.5f) * float2(Soften.w, 1.0f) - lens.yz;
    float inside = lens.x >= Ink.y ? 1.0f - smoothstep(0.98f * Ink.z, Ink.z, length(at)) : 1.0f;
    float cover = 1.0f - tex2D(Mask, uv).r;
    for (int i = 0; i < 12; i++) {
        cover += 1.0f - tex2D(Mask, uv + kDisc[i] * Soften.xy * Soften.z).r;
    }
    float ink = saturate(cover / 13.0f * Ink.x) * inside;
    clip(ink - 0.002f);
    return float4(0.0f, 0.0f, 0.0f, ink);
}
