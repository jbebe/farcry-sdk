// The world wet from rain, over the finished picture: open ground darkened into mud and walls
// darker, and puddles on level ground that mirror the world round them and ring with the drops.
// Every pattern is fixed in the world.
// See src/ground.cpp.

#include "cover.inc.fx"
#include "depth.inc.fx"
#include "hash.inc.fx"
#include "solid.inc.fx"

// x, y: the solid depth's projection; z: how far in front of the solid depth grass and leaves
// stand before they count as such, in metres; w: how far out the wet reaches.
float4 Projection : register(c96);
// x: how wet the open world is; y: how hard it rains; z: how full the puddles are; w: the rain's
// clock, in seconds, wrapped.
float4 Wet : register(c97);
// What EngineFogged reads the engine's fog from.
float4 Fog : register(c98);
float4 FogHeight : register(c99);
// rgb: the sky low down, mirrored in puddles.
float4 SkyLow : register(c100);
// rgb: the sky overhead; w: the brightness above which a pixel is a light or a highlight.
float4 SkyHigh : register(c101);
// xy: the eye's position within the patterns' 256 m repeat; zw: one half-resolution pixel.
float4 Origin : register(c102);
// What cover.inc.fx's Open reads the cover map with.
row_major float4x4 CoverTransform : register(c103);
float4 UpRain : register(c107);

sampler2D Scene : register(s0);
sampler3D ShapeNoise : register(s4);

#define REPEAT 256.0f
// Less wet than this is dry: ground.cpp's kDriest.
#define DRIEST 0.003f
// The puddles' pattern repeats every this many metres, its ragged edges every that many.
#define PUDDLE_SIZE 16.0f
#define PUDDLE_EDGE 4.0f
#define RIPPLE_CELL 0.5f
// The ground's upward normal, as a cosine, where a puddle can begin to lie and where it lies in
// full: six degrees of slope and three; the metres of ground across the view the slope is measured
// over, and the most half-resolution pixels that may take up close.
#define LEVEL_STEEPEST 0.9945f
#define LEVEL_FLAT 0.9986f
#define LEVEL_SPAN 0.75f
#define LEVEL_REACH_MOST 32.0f
// A puddle's reflection: how many steps it marches, the first in metres, how much longer each is
// than the last, and how far off puddles stop reflecting the world.
#define REFLECT_STEPS 20
#define REFLECT_FIRST 0.15f
#define REFLECT_GROWTH 1.25f
#define REFLECT_REACH 60.0f
// Where ripples stop being drawn, in metres.
#define RIPPLES_REACH 30.0f
#define WATER_F0 0.02f

// Metres along the camera's axis to the solid depth at `uv`, or nought where nothing was drawn.
float SolidMetres(float2 uv) {
    return SolidMetresWith(uv, Projection.xy);
}

// Metres along the camera's axis to the solid depth at `uv`, nought off the screen.
float OnScreenMetres(float2 uv) {
    return all(uv == saturate(uv)) ? SolidMetres(uv) : 0.0f;
}

// The step along the surface to whichever neighbour `offset` either side lies closer in depth.
float3 Tangent(float2 uv, float2 offset, float3 centre, float z) {
    float after = OnScreenMetres(uv + offset);
    float before = OnScreenMetres(uv - offset);
    if (after > 0.0f && (before <= 0.0f || abs(after - z) < abs(before - z))) {
        return ViewRay(uv + offset) * after - centre;
    }
    return before > 0.0f ? centre - ViewRay(uv - offset) * before : 0.0f;
}

// The solid surface's normal from its neighbours `reach` half-resolution pixels off, up where there
// is none.
float3 Normal(float2 uv, float3 centre, float z, float reach) {
    float3 across = Tangent(uv, float2(Origin.z * reach, 0.0f), centre, z);
    float3 down = Tangent(uv, float2(0.0f, Origin.w * reach), centre, z);
    float3 normal = cross(across, down);
    normal = dot(normal, normal) > 1e-8f * dot(across, across) * dot(down, down)
                 ? normalize(normal)
                 : float3(0.0f, 0.0f, 1.0f);
    return dot(normal, centre) > 0.0f ? -normal : normal;
}

// Normal's from neighbours `reach` pixels off, blended between whole pixels, so no ring shows where
// a reach that grows with distance steps up.
float3 BlendedNormal(float2 uv, float3 centre, float z, float reach) {
    const float nearer = floor(reach);
    float3 normal = Normal(uv, centre, z, nearer);
    [branch] if (reach > nearer) {
        normal = normalize(lerp(normal, Normal(uv, centre, z, nearer + 1.0f), reach - nearer));
    }
    return normal;
}

// What a mirror at `at` facing along `surface` shows: the world where the reflected look meets it on
// screen, `sky` where it runs off the screen or past everything drawn. Marched in growing steps.
float3 Reflected(float3 at, float3 mirrored, float3 sky) {
    float travelled = 0.0f;
    float step = REFLECT_FIRST;
    [loop] for (int i = 0; i < REFLECT_STEPS; i++) {
        travelled += step;
        step *= REFLECT_GROWTH;
        const float3 q = at + mirrored * travelled;
        const float ahead = dot(q, CameraForward.xyz);
        const float2 uv = OnScreen(q);
        if (ahead <= NEAREST_GEOMETRY || any(uv < 0.0f) || any(uv > 1.0f)) {
            break;
        }
        const float drawn = Metres(uv);
        if (drawn > 0.0f && ahead > drawn && ahead - drawn < max(0.3f, step * 2.0f)) {
            const float2 edge = min(uv, 1.0f - uv);
            const float kept = saturate(min(edge.x, edge.y) * 10.0f);
            return lerp(sky, tex2Dlod(Scene, float4(uv, 0.0f, 0.0f)).rgb, kept);
        }
    }
    return sky;
}

// A cell's own random values, the same wherever the eye stands within the repeat.
float3 CellHash(float2 cell, float cells) {
    return Hash(float3(fmod(cell + cells, cells), 7.0f));
}

// The slope rings from the drops landing on still water put on it, at `at` within the repeat.
float2 Ripples(float2 at) {
    const float cells = REPEAT / RIPPLE_CELL;
    const float2 base = floor(at / RIPPLE_CELL);
    float2 slope = 0.0f;
    [unroll] for (int y = -1; y <= 1; y++) {
        [unroll] for (int x = -1; x <= 1; x++) {
            const float2 cell = base + float2(x, y);
            const float3 h = CellHash(cell, cells);
            // A drop a cell lands twice a second or so in a downpour, once in light rain.
            const float age = frac(Wet.w * lerp(1.0f, 2.4f, Wet.y) * (0.7f + 0.6f * h.z) + h.z);
            const float2 centre = (cell + 0.2f + 0.6f * h.xy) * RIPPLE_CELL;
            const float2 away = at - centre;
            const float d = length(away);
            const float radius = age * 0.35f;
            const float ring = (d - radius) / 0.011f;
            // Gone by seven tenths of the way out.
            const float life = saturate(1.0f - age / 0.7f);
            const float wave = -2.0f * ring * exp(-ring * ring) * life * life;
            slope += away / max(d, 1e-3f) * wave;
        }
    }
    return slope * 0.7f;
}

float4 GroundPS(float3 ray : TEXCOORD0, float2 pixel : VPOS) : COLOR {
    const float2 uv = (pixel + 0.5f) * Lens.zw;
    const float4 scene = tex2Dlod(Scene, float4(uv, 0.0f, 0.0f));
    const float z = Metres(uv);
    clip(z - NEAREST_GEOMETRY);
    const float3 at = ray * z;
    const float distance = length(at);
    // Out of the wet's reach or lost in fog, before the depth reads that follow.
    const float reached = Wet.x * (1.0f - EngineFogged(at, Fog, FogHeight)) *
                          saturate(1.0f - distance / Projection.w);
    [branch] if (reached < DRIEST) {
        clip(-1.0f);
        return 0.0f;
    }

    // Grass and leaves stand in front of the solid depth, or of the sky where it has none, and stay
    // as they are. A read of the half-resolution solid depth can be off by as much as it steps to
    // the next pixel on the same surface, metres on glancing ground and little on a slope facing
    // the eye, so the margin takes that step.
    const float solid = SolidMetres(uv);
    const float2 across = float2(Origin.z, 0.0f);
    const float2 down = float2(0.0f, Origin.w);
    const float rowStep =
        min(abs(OnScreenMetres(uv + down) - solid), abs(OnScreenMetres(uv - down) - solid));
    const float columnStep =
        min(abs(OnScreenMetres(uv + across) - solid), abs(OnScreenMetres(uv - across) - solid));
    const float margin = Projection.z + max(rowStep, columnStep);
    const float foliage = solid > 0.0f ? saturate((solid - z - margin) / Projection.z) : 1.0f;
    const float wet = reached * (1.0f - foliage) * Open(at, CoverTransform, UpRain);
    clip(wet - DRIEST);

    // The normal is the solid depth's own, at the centre of the half-resolution pixel this one lies
    // in, so neighbouring rows read the same surface, and from neighbours farther apart far off,
    // where a glancing look packs the ground's rows tight: blended between whole pixels, so no ring
    // shows where the reach steps up.
    const float2 cell = (floor(uv / Origin.zw) + 0.5f) * Origin.zw;
    const float cellZ = SolidMetres(cell);
    const float3 cellAt = ViewRay(cell) * cellZ;
    const float3 normal = cellZ > 0.0f
                              ? BlendedNormal(cell, cellAt, cellZ, clamp(distance / 15.0f, 1.0f, 4.0f))
                              : float3(0.0f, 0.0f, 1.0f);
    const float ground = smoothstep(0.65f, 0.9f, normal.z);
    const float3 world = float3(at.xy + Origin.xy, at.z);

    // Lamps and highlights keep their light; the rest darkens, open ground into mud.
    const float luma = dot(scene.rgb, float3(0.299f, 0.587f, 0.114f));
    const float lit = smoothstep(SkyHigh.w, 2.0f * SkyHigh.w, luma);
    const float3 mud = lerp(float3(0.78f, 0.78f, 0.78f), float3(0.62f, 0.54f, 0.46f), ground);
    float3 colour = scene.rgb * lerp(1.0f, mud, wet * (1.0f - lit));
    const float grey = dot(colour, float3(0.299f, 0.587f, 0.114f));
    colour = max(lerp(grey, colour, 1.0f + 0.3f * wet), 0.0f);

    // Puddles fill the low places of a pattern fixed in the world, only on ground level within a few
    // degrees. Full puddles take about a third of level ground.
    const float field = tex3Dlod(ShapeNoise, float4(world.xy / PUDDLE_SIZE, 0.31f, 0.0f)).r * 0.8f +
                        tex3Dlod(ShapeNoise, float4(world.xy / PUDDLE_EDGE, 0.67f, 0.0f)).r * 0.2f;
    // The ground round a puddle is soaked darker before the water starts.
    const float fill = 0.72f - Wet.z * 0.2f;
    float puddle = smoothstep(fill, fill + 0.06f, field) * wet;
    float rim = smoothstep(fill - 0.05f, fill, field) * wet;
    // How level the ground is over a span of it, between points of the surface itself: a few degrees
    // of slope are lost in the depth's steps between neighbouring pixels.
    [branch] if (rim > 0.0f && cellZ > 0.0f) {
        const float pixel = cellZ * length(ViewRay(cell + float2(Origin.z, 0.0f)) - ViewRay(cell));
        const float reach = clamp(LEVEL_SPAN / max(pixel, 1e-4f), 1.0f, LEVEL_REACH_MOST);
        const float level = ground * smoothstep(LEVEL_STEEPEST, LEVEL_FLAT,
                                                BlendedNormal(cell, cellAt, cellZ, reach).z);
        puddle *= level;
        rim *= level;
    } else {
        puddle = 0.0f;
        rim = 0.0f;
    }
    colour *= 1.0f - 0.25f * rim;

    // Only a puddle is a mirror: the depth's normals are flat across each triangle, and any shine
    // they gave would show every one. Its water lies flat, rung by the drops, over darker ground.
    [branch] if (puddle > 0.01f) {
        float3 surface = float3(0.0f, 0.0f, 1.0f);
        if (Wet.y > 0.0f && distance < RIPPLES_REACH) {
            surface = normalize(float3(Ripples(world.xy) * saturate(1.0f - distance / RIPPLES_REACH),
                                       1.0f));
        }
        const float3 view = at / max(distance, 1e-3f);
        const float3 mirrored = reflect(view, surface);
        float3 sky = lerp(SkyLow.rgb, SkyHigh.rgb, saturate(mirrored.z * 2.0f));
        if (distance < REFLECT_REACH) {
            sky = lerp(sky, Reflected(at, mirrored, sky),
                       saturate(2.0f - 2.0f * distance / REFLECT_REACH));
        }
        const float facing = saturate(dot(-view, surface));
        const float fresnel = WATER_F0 + (1.0f - WATER_F0) * pow(1.0f - facing, 5.0f);
        colour = lerp(colour * lerp(1.0f, 0.6f, puddle), sky, fresnel * puddle * wet);
    }
    return float4(colour, scene.a);
}
