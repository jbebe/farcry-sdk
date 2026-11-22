// The engine's rain cover map, as src/engine/rain_cover.h hands it to shaders.

sampler2D CoverMap : register(s2);

// Nought under cover, one in the open or beyond the map, at `at` from the eye: `transform` takes a
// point to the map's clip, `upRain` holds the step up the rain the cover is read at and half a
// texel of the map.
float Open(float3 at, float4x4 transform, float4 upRain) {
    const float3 cover = mul(transform, float4(at + upRain.xyz, 1.0f)).xyz;
    const float inside = step(max(abs(cover.x), abs(cover.y)), 0.98f);
    const float2 uv = cover.xy * float2(0.5f, -0.5f) + 0.5f + upRain.w;
    return lerp(1.0f, tex2Dlod(CoverMap, float4(uv, cover.z, 0.0f)).r, inside);
}
