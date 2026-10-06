// The scope's shadow: the dark crescent that comes in from behind the lens's rim as the eye runs
// ahead of the scope. See src/scope_shadow.cpp.

// xy: where the shadow's clear circle has moved to from the lens's centre, in lens radii, y down.
// z: how far the eye has settled into the scope. w: the screen's width over its height.
float4 Shadow : register(c0);
// x: the width of the shadow's soft edge, y: how dark it gets. z: how far the glass reaches, w: the
// shadow's clear circle's radius, both in lens radii.
float4 Edge : register(c1);
// xy: the lens's centre off the screen's, y down. z: its radius. Both in screen heights.
float4 Lens : register(c2);

static const float2 kCentre = float2(0.5f, 0.5f);

// Multiplies the scene: dark where the lens is not covered by the shadow's clear circle, and only
// through the glass.
float4 ShadowPS(float2 uv : TEXCOORD0) : COLOR0 {
    float2 at = ((uv - kCentre) * float2(Shadow.w, 1.0f) - Lens.xy) / Lens.z;
    float glass = 1.0f - smoothstep(1.0f, Edge.z, length(at));
    float cut = smoothstep(Edge.w - Edge.x, Edge.w, length(at - Shadow.xy));
    float shade = 1.0f - Edge.y * cut * glass * Shadow.z;
    clip(0.999f - shade);
    return float4(shade, shade, shade, 1.0f);
}
