// The scope's shadow: the dark crescent that comes in from behind the lens's rim as the eye runs
// ahead of the scope. See src/scope_shadow.cpp.

// xy: where the shadow's clear circle has moved to from the lens's centre, in lens radii, y down.
// z: how far the eye has settled into the scope. w: the screen's width over its height.
float4 Shadow : register(c0);
// x: the width of the shadow's soft edge, in lens radii. y: how dark it gets. z: the smallest radius
// taken for a lens, in screen heights. w: the shadow's clear circle's radius, in lens radii.
float4 Edge : register(c1);
// x: the stored depth the housing is nearer than; one for the scope as the engine draws it, less
// where it is cut.
float4 Hole : register(c2);

// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);
// x: the lens radius. yz: its centre off the screen's, y down. All in screen heights.
sampler2D Lens : register(s5);

static const float2 kCentre = float2(0.5f, 0.5f);

// Multiplies the scene: dark where the lens is not covered by the shadow's clear circle, and only
// through the glass, never over the housing in front of it.
float4 ShadowPS(float2 uv : TEXCOORD0) : COLOR0 {
    float3 lens = tex2D(Lens, kCentre).xyz;
    float radius = lens.x;
    float2 at = ((uv - kCentre) * float2(Shadow.w, 1.0f) - lens.yz) / max(radius, 0.0001f);
    float glass = (1.0f - smoothstep(1.0f, 1.05f, length(at))) *
                  (tex2D(WeaponDepth, uv).r < Hole.x ? 0.0f : 1.0f);
    float cut = smoothstep(Edge.w - Edge.x, Edge.w, length(at - Shadow.xy));
    float shade = 1.0f - Edge.y * cut * glass * Shadow.z * (radius >= Edge.z ? 1.0f : 0.0f);
    clip(0.999f - shade);
    return float4(shade, shade, shade, 1.0f);
}
