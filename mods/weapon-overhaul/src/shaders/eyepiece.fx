// The eyepiece's two screen passes: the gun's depth copied where it can be read back, and the
// opening marked for the reticle to be drawn through.

// x: how far the mask reaches, in the opening's radii. y: the screen's width over its height.
float4 Mask : register(c0);

// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);
// The opening as ScopeLens found it: x its radius, yz its centre off the screen's, y down,
// all in screen heights.
sampler2D Lens : register(s5);

// Into a float target, which unlike the depth texture can be read back.
float4 DepthPS(float2 uv : TEXCOORD0) : COLOR0 {
    return tex2D(WeaponDepth, uv).r;
}

// Nothing but the stencil, inside the opening.
float4 MaskPS(float2 uv : TEXCOORD0) : COLOR0 {
    float3 lens = tex2D(Lens, float2(0.5f, 0.5f)).xyz;
    float2 at = (uv - 0.5f) * float2(Mask.y, 1.0f) - lens.yz;
    clip(Mask.x * lens.x - length(at));
    return 0.0f;
}
