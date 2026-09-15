// Ground-truth ambient occlusion (Jimenez et al. 2016) at half resolution, from a depth of the world
// without its grass and leaves. See src/occlusion.cpp for the passes.

#include "depth.inc.fx"

#define SLICES 2
#define STEPS 6
#define PI 3.14159265f
#define HALF_PI 1.57079633f

// x: the solid depth's depth scale, with w's sign folded in. y: its depth offset. w: how far in
// front of the solid depth, in metres, a pixel stops taking its occlusion.
float4 Projection : register(c96);
// x: the radius looked within, in metres. y: the most half-resolution pixels that radius may cover.
// z: the distance by which the occlusion has faded out. w: how much light full occlusion takes.
float4 Ambient : register(c97);
// xy: one half-resolution pixel, in texture coordinates. z: the half-resolution pixels a metre
// covers, one metre away.
float4 HalfPixel : register(c98);
// xy: the blur's axis, one half-resolution pixel long.
float4 Blur : register(c99);

// The half-resolution occlusion: red is the share of the sky a point sees.
sampler2D Occlusion : register(s0);
// The world's hardware depth without foliage, at half resolution.
sampler2D SolidDepth : register(s1);

float2 HalfUv(float2 halfPixel) {
    return (halfPixel + 0.5f) * HalfPixel.xy;
}

// Metres along the camera's axis to the solid depth at `uv`, or nought where nothing was drawn.
float SolidMetres(float2 uv) {
    float stored = tex2Dlod(SolidDepth, float4(uv, 0.0f, 0.0f)).r;
    return stored < 1.0f ? abs(Projection.y / (stored - Projection.x)) : 0.0f;
}

// How far apart two depths may be, in metres at distance `z`, and still be one surface.
float Tolerance(float z) {
    return 0.05f + 0.02f * z;
}

// A four by four ordered pattern, fixed to the pixel.
float Bayer2(float2 at) {
    at = floor(at);
    return frac(at.x / 2.0f + at.y * at.y * 0.75f);
}

float Bayer4(float2 at) {
    return Bayer2(at * 0.5f) * 0.25f + Bayer2(at);
}

// The step along the surface to whichever neighbour `offset` either side lies closer in depth.
// Nothing where neither side holds geometry.
float3 Tangent(float2 uv, float2 offset, float3 centre, float z) {
    float after = SolidMetres(uv + offset);
    float before = SolidMetres(uv - offset);
    if (after > 0.0f && (before <= 0.0f || abs(after - z) < abs(before - z))) {
        return ViewRay(uv + offset) * after - centre;
    }
    return before > 0.0f ? centre - ViewRay(uv - offset) * before : 0.0f;
}

// The surface's normal, facing the eye, from the neighbouring half-resolution pixels. A point with
// no neighbour to take it from is taken to face up. Whether the tangents span a surface is judged
// against their own lengths, which shrink below a millimetre right up against a wall.
float3 Normal(float2 uv, float3 centre, float z) {
    float3 across = Tangent(uv, float2(HalfPixel.x, 0.0f), centre, z);
    float3 down = Tangent(uv, float2(0.0f, HalfPixel.y), centre, z);
    float3 normal = cross(across, down);
    normal = dot(normal, normal) > 1e-8f * dot(across, across) * dot(down, down)
                 ? normalize(normal)
                 : float3(0.0f, 0.0f, 1.0f);
    return dot(normal, centre) > 0.0f ? -normal : normal;
}

// The cosine-weighted visibility between the view direction and horizon angle `h`, in a slice
// whose projected normal lies at angle `n`.
float Arc(float h, float n) {
    return 0.25f * (-cos(2.0f * h - n) + cos(n) + 2.0f * h * sin(n));
}

// Lifts one side's horizon cosine to the sample at `uv`, weighted down with distance so nothing past
// the radius occludes.
float Horizon(float horizon, float lowest, float2 uv, float3 centre, float3 toEye, float radius) {
    float z = SolidMetres(uv);
    if (z <= 0.0f) {
        return horizon;
    }
    float3 delta = ViewRay(uv) * z - centre;
    float distance = length(delta);
    float weight = saturate(1.0f - distance * distance / (radius * radius));
    return max(horizon, lerp(lowest, dot(delta, toEye) / max(distance, 0.0001f), weight));
}

float4 GtaoPS(float2 screen : VPOS) : COLOR0 {
    float2 uv = HalfUv(screen);
    float z = SolidMetres(uv);
    if (z <= 0.0f || z >= Ambient.z) {
        return 1.0f;
    }
    float3 centre = ViewRay(uv) * z;
    float3 normal = Normal(uv, centre, z);
    float3 toEye = -normalize(centre);

    float pixelsPerMetre = HalfPixel.z / z;
    float radius = min(Ambient.x, Ambient.y / pixelsPerMetre);
    float radiusPixels = radius * pixelsPerMetre;
    if (radiusPixels < 1.0f) {
        return 1.0f;
    }

    float rotation = Bayer4(screen);
    float jitter = Bayer4(screen.yx + 1.0f);
    float visibility = 0.0f;
    [loop] for (int s = 0; s < SLICES; s++) {
        float phi = ((float)s + rotation) * PI / SLICES;
        // Screen direction with y down, and the world direction it runs along.
        float2 direction = float2(cos(phi), sin(phi));
        float3 along = CameraRight.xyz * direction.x - CameraUp.xyz * direction.y;
        float3 ortho = along - dot(along, toEye) * toEye;
        float3 axis = normalize(cross(ortho, toEye));
        float3 projected = normal - axis * dot(normal, axis);
        float projectedLength = length(projected);
        float cosN = saturate(dot(projected, toEye) / max(projectedLength, 0.0001f));
        float n = sign(dot(ortho, projected)) * acos(cosN);

        // Angles run from the view direction, positive toward `along`. The lowest horizons lie a
        // right angle either side of the normal.
        float lowestAlong = -sin(n);
        float lowestAgainst = sin(n);
        float horizonAlong = lowestAlong;
        float horizonAgainst = lowestAgainst;
        [loop] for (int j = 0; j < STEPS; j++) {
            float t = ((float)j + jitter) / STEPS;
            float2 offset = direction * (t * t * radiusPixels + 1.0f) * HalfPixel.xy;
            horizonAlong = Horizon(horizonAlong, lowestAlong, uv + offset, centre, toEye, radius);
            horizonAgainst =
                Horizon(horizonAgainst, lowestAgainst, uv - offset, centre, toEye, radius);
        }
        float hAlong = min(acos(clamp(horizonAlong, -1.0f, 1.0f)), n + HALF_PI);
        float hAgainst = max(-acos(clamp(horizonAgainst, -1.0f, 1.0f)), n - HALF_PI);
        visibility += projectedLength * (Arc(hAlong, n) + Arc(hAgainst, n));
    }
    visibility = saturate(visibility / SLICES);
    return lerp(visibility, 1.0f, smoothstep(Ambient.z * 0.5f, Ambient.z, z));
}

// One axis of a blur that keeps to a surface. Its weights cancel the four by four pattern the
// occlusion was sampled with, and the two passes together all of it.
float4 BlurPS(float2 screen : VPOS) : COLOR0 {
    float z = SolidMetres(HalfUv(screen));
    [branch] if (z <= 0.0f || z >= Ambient.z) {
        return 1.0f;
    }
    float tolerance = Tolerance(z);
    float sum = 0.0f;
    float weight = 0.0f;
    [unroll] for (int i = -2; i <= 2; i++) {
        float2 at = HalfUv(screen + Blur.xy * i);
        float w = (abs(i) == 2 ? 0.5f : 1.0f) * exp(-abs(SolidMetres(at) - z) / tolerance);
        sum += tex2Dlod(Occlusion, float4(at, 0.0f, 0.0f)).r * w;
        weight += w;
    }
    return sum / weight;
}

// What the world is multiplied by: the occlusion around this pixel on its own surface, taken less
// the farther in front of the solid depth the pixel stands, so grass darkens at its roots. The
// weapon, which the engine's depth holds nothing for, is left alone.
float4 ApplyPS(float2 screen : VPOS) : COLOR0 {
    float z = Metres((screen + 0.5f) * Lens.zw);
    clip(z - NEAREST_GEOMETRY);
    float tolerance = Tolerance(z);
    float2 at = screen * 0.5f - 0.25f;
    float2 base = floor(at);
    float2 along = at - base;
    float sum = 0.0f;
    float weight = 0.0f;
    float inFront = 1e10f;
    [unroll] for (int y = 0; y < 2; y++) {
        [unroll] for (int x = 0; x < 2; x++) {
            float2 tap = HalfUv(base + float2(x, y));
            float2 share = lerp(1.0f - along, along, float2(x, y));
            float solid = SolidMetres(tap);
            float w = (share.x * share.y + 0.001f) *
                      (exp(-abs(solid - z) / tolerance) + 0.0001f);
            sum += tex2Dlod(Occlusion, float4(tap, 0.0f, 0.0f)).r * w;
            weight += w;
            inFront = min(inFront, solid > 0.0f ? max(solid - z, 0.0f) : 1e10f);
        }
    }
    float reach = saturate(1.0f - inFront / Projection.w);
    return 1.0f - Ambient.w * (1.0f - sum / weight) * reach;
}
