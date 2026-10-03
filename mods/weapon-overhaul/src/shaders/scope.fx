// The scope's shadow: the dark crescent that comes in from the lens's rim as the eye runs ahead of
// the scope. See src/scope_shadow.cpp for the passes.

// xy: where the clear part of the lens has moved to from its centre, in lens radii, y down. z: how
// far the eye has settled into the scope. w: the screen's width over its height.
float4 Shadow : register(c0);
// x: the nearest radius looked at for the housing, y: the step between radii, both in screen
// heights. z: the screen's height over its width. w: how far the lens moves toward this frame's.
float4 Search : register(c1);
// x: the width of the shadow's soft edge, in lens radii. y: how dark it gets. z: the smallest radius
// taken for a lens, in screen heights.
float4 Edge : register(c2);

// The first housing hit along each direction.
sampler2D Radii : register(s0);
// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);
// x: the lens radius, nought before there is one. yz: its centre off the screen's, y down. All in
// screen heights.
sampler2D Lens : register(s5);

#define DIRECTIONS 24
#define CHORDS 12
#define STEPS 48
#define TWO_PI 6.28318531f

static const float2 kCentre = float2(0.5f, 0.5f);

float Radius(int direction) {
    return tex2D(Radii, float2((direction + 0.5f) / DIRECTIONS, 0.5f)).r;
}

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

// The lens from twelve chords through the screen's centre, each two opposite hits on its rim. A
// chord's midpoint is the lens's centre seen along it, and the product of its two halves is the
// radius squared less the centre's distance squared. Chords a reticle line cut short are left out.
// The centre is taken as found, so it moves with the scope; only the radius eases.
float4 LensPS(float2 uv : TEXCOORD0) : COLOR0 {
    float longest = 0.0f;
    for (int i = 0; i < CHORDS; i++) {
        float ahead = Radius(i);
        float behind = Radius(i + CHORDS);
        longest = max(longest, ahead > 0.0f && behind > 0.0f ? ahead + behind : 0.0f);
    }
    float2 centre = 0.0f;
    float product = 0.0f;
    float chords = 0.0f;
    for (int c = 0; c < CHORDS; c++) {
        float ahead = Radius(c);
        float behind = Radius(c + CHORDS);
        float whole = ahead > 0.0f && behind > 0.0f && ahead + behind > 0.8f * longest ? 1.0f : 0.0f;
        float angle = (c + 0.5f) / DIRECTIONS * TWO_PI;
        centre += whole * (ahead - behind) * 0.5f * float2(cos(angle), -sin(angle));
        product += whole * ahead * behind;
        chords += whole;
    }
    centre *= 2.0f / max(chords, 1.0f);
    float3 found = float3(sqrt(product / max(chords, 1.0f) + dot(centre, centre)), centre);

    float3 previous = tex2D(Lens, kCentre).xyz;
    float radius = previous.x > 0.0f ? lerp(previous.x, found.x, Search.w) : found.x;
    return float4(chords >= 3.0f ? float3(radius, found.yz) : previous, 1.0f);
}

// Multiplies the scene: dark where the lens is not covered by its clear part.
float4 ShadowPS(float2 uv : TEXCOORD0) : COLOR0 {
    float3 lens = tex2D(Lens, kCentre).xyz;
    float radius = lens.x;
    float2 at = ((uv - kCentre) * float2(Shadow.w, 1.0f) - lens.yz) / max(radius, 0.0001f);
    float inLens = 1.0f - smoothstep(0.97f, 1.0f, length(at));
    float cut = smoothstep(1.0f - Edge.x, 1.0f, length(at - Shadow.xy));
    float shade = 1.0f - Edge.y * cut * inLens * Shadow.z * (radius >= Edge.z ? 1.0f : 0.0f);
    clip(0.999f - shade);
    return float4(shade, shade, shade, 1.0f);
}
