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
// x: the stored depth the housing is nearer than; one for the scope as the engine draws it, less
// where it is cut.
float4 Hole : register(c3);

// The first housing hit along each direction.
sampler2D Radii : register(s0);
// The weapon's hardware depth at half resolution, one where the weapon is not.
sampler2D WeaponDepth : register(s3);
// x: the lens radius, nought before there is one. yz: its centre off the screen's, y down. All in
// screen heights. Last frame's while it is being found, this frame's after.
sampler2D Lens : register(s5);

#define DIRECTIONS 24
#define STEPS 64
#define TWO_PI 6.28318531f
// The fewest hits a lens is fitted to.
#define FEWEST 5

static const float2 kCentre = float2(0.5f, 0.5f);

// The lens as last found. Read with tex2Dlod, since ps_3_0 shaders here have no texture coordinates.
float3 LastLens() {
    return tex2Dlod(Lens, float4(kCentre, 0.0f, 0.0f)).xyz;
}

// Along this pixel's direction, half a step off either axis so a cross reticle is not met, the
// first of the gun outward from the lens's last centre, which keeps the search inside the lens
// however far the scope has moved: the inside of the housing, a reticle line, or nothing, and
// nothing once the direction has left the screen. ps_3_0, since its reads start from a texture.
float4 RadiusPS(float2 pixel : VPOS) : COLOR0 {
    float angle = (pixel.x + 0.5f) / DIRECTIONS * TWO_PI;
    float2 direction = float2(cos(angle) * Search.z, -sin(angle));
    float2 origin = kCentre + LastLens().yz * float2(Search.z, 1.0f);
    float hit = 0.0f;
    for (int i = 0; i < STEPS; i++) {
        float radius = Search.x + i * Search.y;
        float2 at = origin + direction * radius;
        float stored = tex2Dlod(WeaponDepth, float4(at, 0.0f, 0.0f)).r;
        bool housing = all(abs(at - kCentre) < 0.5f) && stored < Hole.x;
        hit = hit > 0.0f ? hit : (housing ? radius : 0.0f);
    }
    return float4(hit, 0.0f, 0.0f, 1.0f);
}

// A direction's hit off the last centre, in screen heights, x right and y down. z: one if there is
// one.
float3 Hit(int direction) {
    float hit = tex2Dlod(Radii, float4((direction + 0.5f) / DIRECTIONS, 0.5f, 0.0f, 0.0f)).r;
    float angle = (direction + 0.5f) / DIRECTIONS * TWO_PI;
    return float3(hit * float2(cos(angle), -sin(angle)), hit > 0.0f ? 1.0f : 0.0f);
}

float Determinant(float3 a, float3 b, float3 c) {
    return dot(a, cross(b, c));
}

// The circle nearest the hits by least squares on x² + y² + Dx + Ey + F = 0, taking only the hits
// within `within` of the circle `near`'s radius from its centre, or every hit while `near` has none.
// xy: its centre, z: its radius, w: how many hits it was fitted to.
float4 Fit(float3 near, float within) {
    float3 xRow = 0.0f;
    float3 yRow = 0.0f;
    float3 nRow = 0.0f;
    float3 sums = 0.0f;
    for (int i = 0; i < DIRECTIONS; i++) {
        float3 hit = Hit(i);
        float2 p = hit.xy;
        float off = abs(length(p - near.xy) - near.z);
        float taken = hit.z * (near.z > 0.0f && off > within * near.z ? 0.0f : 1.0f);
        float squared = dot(p, p);
        xRow += taken * float3(p.x * p.x, p.x * p.y, p.x);
        yRow += taken * float3(p.x * p.y, p.y * p.y, p.y);
        nRow += taken * float3(p.x, p.y, 1.0f);
        sums -= taken * float3(p.x * squared, p.y * squared, squared);
    }
    float whole = Determinant(xRow, yRow, nRow);
    if (abs(whole) < 1e-12f) {
        return 0.0f;
    }
    float d = Determinant(float3(sums.x, xRow.yz), float3(sums.y, yRow.yz), float3(sums.z, nRow.yz));
    float e = Determinant(float3(xRow.x, sums.x, xRow.z), float3(yRow.x, sums.y, yRow.z),
                          float3(nRow.x, sums.z, nRow.z));
    float f = Determinant(float3(xRow.xy, sums.x), float3(yRow.xy, sums.y), float3(nRow.xy, sums.z));
    float2 centre = -0.5f * float2(d, e) / whole;
    return float4(centre, sqrt(max(dot(centre, centre) - f / whole, 0.0f)), nRow.z);
}

// The lens as the circle through the hits, so a rim partly off the screen still fits, refitted to
// the hits near it twice over so a reticle line met is left out. The centre is taken as found, so it
// moves with the scope; only the radius eases. Drawn as a full quad, for ps_3_0.
float4 LensPS() : COLOR0 {
    float4 rough = Fit(0.0f, 0.0f);
    float4 closer = Fit(rough.xyz, 0.25f);
    float4 found = Fit(closer.xyz, 0.1f);

    // A search that found no lens starts again from the screen's centre.
    float3 previous = LastLens();
    float radius = previous.x > 0.0f ? lerp(previous.x, found.z, Search.w) : found.z;
    bool lens = found.w >= FEWEST && found.z >= Edge.z;
    return float4(lens ? float3(radius, previous.yz + found.xy) : float3(previous.x, 0.0f, 0.0f), 1.0f);
}

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
