// The eyepiece's two screen passes: the gun's depth copied where it can be read back, and the
// opening marked for the reticle to be drawn through.

// x, y: the opening's centre off the screen's, y down, z: its radius, both in screen heights.
// w: the screen's width over its height.
float4 Opening : register(c0);

// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);

// Into a float target, which unlike the depth texture can be read back.
float4 DepthPS(float2 uv : TEXCOORD0) : COLOR0 {
    return tex2D(WeaponDepth, uv).r;
}

// Nothing but the stencil, inside the opening.
float4 MaskPS(float2 uv : TEXCOORD0) : COLOR0 {
    float2 at = (uv - 0.5f) * float2(Opening.w, 1.0f) - Opening.xy;
    clip(Opening.z - length(at));
    return 0.0f;
}
