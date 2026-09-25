// What grass and leaves take from the engine operation for operation: its fog and the fade of its
// shadow map.

// w  how the engine scales distances
float4 CameraPosition : register(c45);
float3 FogColorVector : register(c48);
float3 FogValues : register(c51);
float4 FogHeightValues : register(c52);

// Where a point `toEye` from the eye stands on the fog's colour ramp.
float FogRampPlace(float3 toEye)
{
    float facing = dot(-normalize(toEye.xy), FogColorVector.xy);
    return (facing * -0.0675179511f + 0.5f) * sqrt(max(1.0f - facing, 0.0f));
}

float FogAmount(float distance, float height)
{
    return saturate(distance * FogValues.x + FogValues.y) * FogValues.z *
           (saturate(height * FogHeightValues.x + FogHeightValues.y) * FogHeightValues.z +
            FogHeightValues.w);
}

// How much of the shadow shows at a point `projected` into the shadow map, and how far it is from
// the map's centre in extents. A single slice keeps the map's extent in `fade0` and the fade over
// depth in `fade1`; cascades keep the fade over depth in `fade0` and their extent, which differs on
// each side of the centre, in `fade1`.
float ShadowShown(float3 projected, bool cascaded, float4 edgeFade, float4 fade0, float4 fade1,
                  out float centred)
{
    float2 edge = cascaded ? projected.xy * lerp(fade1.zw, fade1.xy, projected.xy >= 0.0f)
                           : projected.xy * fade0.xy + fade0.zw;
    float4 depth = cascaded ? fade0 : fade1;
    centred = max(abs(edge.x), abs(edge.y));
    return 1.0f - saturate(depth.x * projected.z + depth.y) *
                  saturate(centred * edgeFade.z + edgeFade.w);
}
