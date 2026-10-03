// The scope's shadow: the dark crescent that comes in from behind the lens's rim as the eye runs
// ahead of the scope. See src/scope_shadow.cpp for the passes.

// xy: where the shadow's clear circle has moved to from the lens's centre, in lens radii, y down.
// z: how far the eye has settled into the scope. w: the screen's width over its height.
float4 Shadow : register(c0);
// x: the nearest radius looked at for the housing, y: the step between radii, both in screen
// heights. z: the screen's height over its width. w: how far the lens radius moves toward this
// frame's.
float4 Search : register(c1);
// x: the width of the shadow's soft edge, in lens radii. y: how dark it gets. z: the smallest radius
// taken for a lens, in screen heights. w: the shadow's clear circle's radius, in lens radii.
float4 Edge : register(c2);
// xyz: the lens when it is known, as the lens target holds it. w: one where the glass is told from
// the gun's depth, nought where the lens is known and all of it glass.
float4 Known : register(c3);

// The first housing hit along each direction.
sampler2D Radii : register(s0);
// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);
// x: the lens radius, nought before there is one. yz: its centre off the screen's, y down. All in
// screen heights. Last frame's while it is being found, this frame's after.
sampler2D Lens : register(s5);

#define DIRECTIONS 24
#define CHORDS 12
#define STEPS 64
#define TWO_PI 6.28318531f

static const float2 kCentre = float2(0.5f, 0.5f);

float Radius(int direction) {
    return tex2D(Radii, float2((direction + 0.5f) / DIRECTIONS, 0.5f)).r;
}

// The lens's centre as last found, in texture coordinates, which is where the search starts so it
// stays inside the lens however far the scope has moved.
float2 Origin() {
    return kCentre + tex2D(Lens, kCentre).yz * float2(Search.z, 1.0f);
}

// Along this pixel's direction, half a step off either axis so a cross reticle is not met, the
// first of the gun outward from the origin: the inside of the housing, a reticle line, or nothing.
// ps_3_0, since its reads start from one.
float4 RadiusPS(float2 pixel : VPOS) : COLOR0 {
    float angle = (pixel.x + 0.5f) / DIRECTIONS * TWO_PI;
    float2 direction = float2(cos(angle) * Search.z, -sin(angle));
    float2 origin = Origin();
    float hit = 0.0f;
    for (int i = 0; i < STEPS; i++) {
        float radius = Search.x + i * Search.y;
        float stored = tex2Dlod(WeaponDepth, float4(origin + direction * radius, 0.0f, 0.0f)).r;
        hit = hit > 0.0f ? hit : (stored < 1.0f ? radius : 0.0f);
    }
    return float4(hit, 0.0f, 0.0f, 1.0f);
}

// The lens from twelve chords through the origin, each two opposite hits on its rim. A chord's
// midpoint is the lens's centre seen along it, and the product of its two halves is the radius
// squared less the centre's distance squared. Chords a reticle line cut short are left out. The
// centre is taken as found, so it moves with the scope; only the radius eases.
float4 LensPS(float2 uv : TEXCOORD0) : COLOR0 {
    float longest = 0.0f;
    for (int i = 0; i < CHORDS; i++) {
        float ahead = Radius(i);
        float behind = Radius(i + CHORDS);
        longest = max(longest, ahead > 0.0f && behind > 0.0f ? ahead + behind : 0.0f);
    }
    float2 offset = 0.0f;
    float product = 0.0f;
    float chords = 0.0f;
    for (int c = 0; c < CHORDS; c++) {
        float ahead = Radius(c);
        float behind = Radius(c + CHORDS);
        float whole = ahead > 0.0f && behind > 0.0f && ahead + behind > 0.8f * longest ? 1.0f : 0.0f;
        float angle = (c + 0.5f) / DIRECTIONS * TWO_PI;
        offset += whole * (ahead - behind) * 0.5f * float2(cos(angle), -sin(angle));
        product += whole * ahead * behind;
        chords += whole;
    }
    offset *= 2.0f / max(chords, 1.0f);
    float found = sqrt(product / max(chords, 1.0f) + dot(offset, offset));

    // A search that found no lens starts again from the screen's centre.
    float3 previous = tex2D(Lens, kCentre).xyz;
    float radius = previous.x > 0.0f ? lerp(previous.x, found, Search.w) : found;
    bool lens = chords >= 3.0f && found >= Edge.z;
    return float4(lens ? float3(radius, previous.yz + offset) : float3(previous.x, 0.0f, 0.0f), 1.0f);
}

float4 KnownLensPS(float2 uv : TEXCOORD0) : COLOR0 {
    return float4(Known.xyz, 1.0f);
}

// Multiplies the scene: dark where the lens is not covered by the shadow's clear circle, and only
// through the glass, never over the housing in front of it.
float4 ShadowPS(float2 uv : TEXCOORD0) : COLOR0 {
    float3 lens = tex2D(Lens, kCentre).xyz;
    float radius = lens.x;
    float2 at = ((uv - kCentre) * float2(Shadow.w, 1.0f) - lens.yz) / max(radius, 0.0001f);
    float clear = tex2D(WeaponDepth, uv).r < 1.0f ? 0.0f : 1.0f;
    float glass = (1.0f - smoothstep(1.0f, 1.05f, length(at))) * lerp(1.0f, clear, Known.w);
    float cut = smoothstep(Edge.w - Edge.x, Edge.w, length(at - Shadow.xy));
    float shade = 1.0f - Edge.y * cut * glass * Shadow.z * (radius >= Edge.z ? 1.0f : 0.0f);
    clip(0.999f - shade);
    return float4(shade, shade, shade, 1.0f);
}
