// The scope's shadow: the dark crescent that comes in from the lens's rim as the eye drifts off the
// scope's axis. See src/scope_shadow.cpp for the passes.

// xy: where the clear part of the lens has drifted to, in lens radii, y down. z: how far the eye
// has settled into the scope. w: the screen's width over its height.
float4 Shadow : register(c0);
// x: the nearest radius looked at for the housing, y: the step between radii, both in screen
// heights. z: the screen's height over its width. w: how far the lens moves toward this frame's.
float4 Search : register(c1);
// x: the width of the shadow's soft edge, in lens radii. y: how dark it gets. z: the smallest radius
// taken for a lens, in screen heights.
float4 Edge : register(c2);

// The first housing hit along each direction, or the lens radius.
sampler2D Radii : register(s0);
// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);
// The lens radius in screen heights, nought before there is one.
sampler2D Lens : register(s5);

#define DIRECTIONS 24
#define STEPS 48
#define TWO_PI 6.28318531f

static const float2 kCentre = float2(0.5f, 0.5f);

// Along this pixel's direction, half a step off either axis so a cross reticle is not met, the
// first of the gun outward from the centre: the inside of the housing, a reticle line, or nothing.
float4 RadiusPS(float2 uv : TEXCOORD0) : COLOR0 {
    float angle = uv.x * TWO_PI;
    float2 direction = float2(cos(angle) * Search.z, -sin(angle));
    float hit = 0.0f;
    for (int i = 0; i < STEPS; i++) {
        float radius = Search.x + i * Search.y;
        float stored = tex2D(WeaponDepth, kCentre + direction * radius).r;
        hit = hit > 0.0f ? hit : (stored < 1.0f ? radius : 0.0f);
    }
    return float4(hit, 0.0f, 0.0f, 1.0f);
}

// The farthest first hit, which is the housing: reticle lines are nearer, and gaps hit nothing.
float4 LensPS(float2 uv : TEXCOORD0) : COLOR0 {
    float widest = 0.0f;
    for (int i = 0; i < DIRECTIONS; i++) {
        widest = max(widest, tex2D(Radii, float2((i + 0.5f) / DIRECTIONS, 0.5f)).r);
    }
    float previous = tex2D(Lens, kCentre).r;
    float eased = previous > 0.0f ? lerp(previous, widest, Search.w) : widest;
    return float4(widest > 0.0f ? eased : previous, 0.0f, 0.0f, 1.0f);
}

// Multiplies the scene: dark where the lens is not covered by its clear part.
float4 ShadowPS(float2 uv : TEXCOORD0) : COLOR0 {
    float radius = tex2D(Lens, kCentre).r;
    float2 at = (uv - kCentre) * float2(Shadow.w, 1.0f) / max(radius, 0.0001f);
    float inLens = 1.0f - smoothstep(0.97f, 1.0f, length(at));
    float cut = smoothstep(1.0f - Edge.x, 1.0f, length(at - Shadow.xy));
    float shade = 1.0f - Edge.y * cut * inLens * Shadow.z * (radius >= Edge.z ? 1.0f : 0.0f);
    clip(0.999f - shade);
    return float4(shade, shade, shade, 1.0f);
}
