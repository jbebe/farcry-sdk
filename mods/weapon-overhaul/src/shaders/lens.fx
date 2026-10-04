// The scope's lens, found on screen as the hole its housing leaves. See src/scope_lens.cpp for the
// passes.

// x: the nearest radius looked at for the housing, y: the step between radii, both in screen
// heights. z: the screen's height over its width. w: how far the lens radius moves toward this
// frame's.
float4 Search : register(c0);
// x: what the walls' red is below wherever the housing is. y: the smallest radius taken for a lens,
// in screen heights.
float4 Hole : register(c1);

// The first housing hit along each direction.
sampler2D Radii : register(s0);
// The walls at half resolution: the weapon's hardware depth, or a mask of the housing.
sampler2D Walls : register(s3);
// x: the lens radius, nought before there is one. yz: its centre off the screen's, y down. All in
// screen heights. Last frame's.
sampler2D Lens : register(s5);

#define DIRECTIONS 24
#define STEPS 64
#define TWO_PI 6.28318531f
// The fewest hits a lens is fitted to.
#define FEWEST 5

static const float2 kCentre = float2(0.5f, 0.5f);

// The lens as last found.
float3 LastLens() {
    return tex2Dlod(Lens, float4(kCentre, 0.0f, 0.0f)).xyz;
}

// A direction in screen heights, x right and y down, half a step off either axis so a cross
// reticle is not met.
float2 Direction(float index) {
    float angle = (index + 0.5f) / DIRECTIONS * TWO_PI;
    return float2(cos(angle), -sin(angle));
}

// Along this pixel's direction from the lens's last centre, the first of the gun on the screen: the
// inside of the housing, a reticle line, or nothing.
float4 RadiusPS(float2 pixel : VPOS) : COLOR0 {
    float2 direction = Direction(pixel.x) * float2(Search.z, 1.0f);
    float2 origin = kCentre + LastLens().yz * float2(Search.z, 1.0f);
    float hit = 0.0f;
    for (int i = 0; i < STEPS; i++) {
        float radius = Search.x + i * Search.y;
        float2 at = origin + direction * radius;
        float stored = tex2Dlod(Walls, float4(at, 0.0f, 0.0f)).r;
        bool housing = all(abs(at - kCentre) < 0.5f) && stored < Hole.x;
        hit = hit > 0.0f ? hit : (housing ? radius : 0.0f);
    }
    return float4(hit, 0.0f, 0.0f, 1.0f);
}

// A direction's hit off the last centre, in screen heights. z: one if there is one.
float3 Hit(int direction) {
    float hit = tex2Dlod(Radii, float4((direction + 0.5f) / DIRECTIONS, 0.5f, 0.0f, 0.0f)).r;
    return float3(hit * Direction(direction), hit > 0.0f ? 1.0f : 0.0f);
}

float Determinant(float3 a, float3 b, float3 c) {
    return dot(a, cross(b, c));
}

// The circle nearest the hits by least squares on x^2 + y^2 + Dx + Ey + F = 0, taking only the hits
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

// The lens as the circle through the hits, refitted twice to the hits near it. Its radius eases,
// its centre is taken as found, and a search that found none starts again from the screen's centre.
float4 LensPS() : COLOR0 {
    float4 rough = Fit(0.0f, 0.0f);
    float4 closer = Fit(rough.xyz, 0.25f);
    float4 found = Fit(closer.xyz, 0.1f);

    float3 previous = LastLens();
    float radius = previous.x > 0.0f ? lerp(previous.x, found.z, Search.w) : found.z;
    bool lens = found.w >= FEWEST && found.z >= Hole.y;
    return float4(lens ? float3(radius, previous.yz + found.xy) : float3(previous.x, 0.0f, 0.0f), 1.0f);
}
