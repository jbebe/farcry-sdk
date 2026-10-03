// The eyepiece's two screen passes: the gun's depth copied where it can be read back, and the
// reticle laid over the finished frame, black and soft, inside the opening.

// x, y: one pixel across and down, in texture coordinates. z: the reticle's softness, a radius in
// pixels. w: the screen's width over its height.
float4 Soften : register(c0);
// x: how much the softened coverage is strengthened. y: the smallest radius taken for a lens, in
// screen heights.
float4 Ink : register(c1);

// The reticle's mask, nought where it was drawn.
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

// Black, as much as the softened reticle covers the pixel, and only through the opening.
float4 ReticlePS(float2 uv : TEXCOORD0) : COLOR0 {
    float3 lens = tex2D(Lens, float2(0.5f, 0.5f)).xyz;
    float2 at = (uv - 0.5f) * float2(Soften.w, 1.0f) - lens.yz;
    float inside = lens.x >= Ink.y ? 1.0f - smoothstep(0.98f * lens.x, lens.x, length(at)) : 1.0f;
    float cover = 1.0f - tex2D(Mask, uv).r;
    for (int i = 0; i < 12; i++) {
        cover += 1.0f - tex2D(Mask, uv + kDisc[i] * Soften.xy * Soften.z).r;
    }
    float ink = saturate(cover / 13.0f * Ink.x) * inside;
    clip(ink - 0.002f);
    return float4(0.0f, 0.0f, 0.0f, ink);
}
