// The water's waves while it rains, in place of its own normal map: rings from the drops, a few
// apart in light rain and crowding into a chop in a downpour, over a faint swell. Tiles in both
// directions, and stores a normal as the water's own map does: x in alpha, y in green.

#include "hash.inc.fx"

// x: how hard it rains; y: the rain's clock, in seconds; z: ring cells across the map; w: how
// steep a ring's slope is at its crest.
float4 Rain : register(c91);
// xy: one texel of the map.
float4 Texel : register(c92);

sampler3D ShapeNoise : register(s0);

// The slope the rings of one grid of cells put on the water at `uv`.
float2 Rings(float2 uv, float cells, float rate, float seed) {
    const float2 base = floor(uv * cells);
    float2 slope = 0.0f;
    [unroll] for (int y = -1; y <= 1; y++) {
        [unroll] for (int x = -1; x <= 1; x++) {
            const float2 cell = base + float2(x, y);
            const float3 h = Hash(float3(fmod(cell + cells, cells), seed));
            const float age = frac(Rain.y * rate * (0.6f + 0.8f * h.z) + h.x);
            const float2 centre = (cell + 0.2f + 0.6f * h.xy) / cells;
            const float2 away = uv - centre;
            const float d = length(away) * cells;
            const float radius = age * 0.9f;
            const float ring = (d - radius) / 0.035f;
            // Gone by seven tenths of the way out.
            const float life = saturate(1.0f - age / 0.7f);
            float wave = -2.0f * ring * exp(-ring * ring) * life * life;
            // The crown the drop throws up as it lands, steep enough to catch the sky white.
            const float crown = d / 0.08f;
            wave -= 3.0f * crown * exp(-crown * crown) * saturate(1.0f - age / 0.12f);
            slope += away / max(length(away), 1e-5f) * wave;
        }
    }
    return slope;
}

float4 RipplesPS(float2 pixel : VPOS) : COLOR {
    const float2 uv = (pixel + 0.5f) * Texel.xy;
    const float cells = Rain.z;
    float2 slope = Rings(uv, cells, lerp(1.2f, 2.6f, Rain.x), 3.0f);
    // A downpour lands a second, finer crowd of drops between the first.
    [branch] if (Rain.x > 0.4f) {
        slope += Rings(uv, cells * 2.0f, 3.5f, 5.0f) * smoothstep(0.4f, 1.0f, Rain.x) * 0.7f;
    }
    // The swell the rain falls on, tiling with the map.
    const float3 at = float3(uv, Rain.y * 0.02f);
    const float2 swell = float2(tex3Dlod(ShapeNoise, float4(at, 0.0f)).r,
                                tex3Dlod(ShapeNoise, float4(at.yxz + 0.5f, 0.0f)).r) - 0.5f;
    const float3 normal = normalize(float3(slope * Rain.w + swell * 0.3f, 1.0f));
    return float4(0.0f, normal.y * 0.5f + 0.5f, 0.0f, normal.x * 0.5f + 0.5f);
}
