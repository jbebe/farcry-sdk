// The clouds, drawn over the world's own sky pass in place of the engine's.
//
// The output convention is the engine's own cloud layer's, so that the bloom and tone mapping
// after it see what they saw before: colour premultiplied and scaled by the exposure, alpha the
// transmittance, blended with (one, source alpha).

// xyz: where the camera is, in world space with Z up. w: the frame's exposure.
float4 Eye : register(c71);

// x: the altitude the slab sits at, not a height above the camera. y: how deep it is. z: how much
// of the sun's light reaches it. w: how wide one cell of the test pattern is.
float4 Slab : register(c72);

// The light the engine would have lit its own clouds by, this frame.
float4 SunColour : register(c73);
float4 AmbientColour : register(c74);

// x: how far out the slab is still drawn. y: over what distance it fades away before that.
float4 Range : register(c75);

struct VertexIn {
    float4 position : POSITION0;
    float3 ray : TEXCOORD0;
};

struct VertexOut {
    float4 position : POSITION0;
    float3 ray : TEXCOORD0;
};

// The quad arrives in clip space already, carrying the world-space ray through each corner, so
// there is nothing to transform and no vertex constant to depend on.
VertexOut MainVS(VertexIn input) {
    VertexOut output;
    output.position = input.position;
    output.ray = input.ray;
    return output;
}

// Nothing volumetric yet: one flat slab in a checkerboard, which is the shape that shows whether
// the ray and the camera are right. If either is wrong the pattern swims with the view instead of
// staying put over the ground.
//
// The slab is at an altitude, so walking uphill or jumping moves the camera under it rather than
// carrying it along, and it stops well before the horizon, where cells would otherwise shrink past
// a pixel and shimmer at every step.
float4 MainPS(float3 rayIn : TEXCOORD0) : COLOR0 {
    float3 ray = normalize(rayIn);
    float rise = Slab.x - Eye.z;

    // Clamped rather than branched around, because the pattern below is filtered by the screen
    // derivatives of its own coordinate, and a derivative taken inside a branch is undefined.
    float up = max(ray.z, 0.001f);
    float travel = rise / up;
    float2 uv = (Eye.xy + ray.xy * travel) / Slab.w;

    // The checker averaged over the pixel it covers rather than sampled at its centre. A hard edge
    // here would crawl across the screen at every step, which reads as the whole grid shaking.
    float2 footprint = max(abs(ddx(uv)), abs(ddy(uv))) + 0.001f;
    float2 filtered = 2.0f * (abs(frac((uv - 0.5f * footprint) * 0.5f) - 0.5f) -
                              abs(frac((uv + 0.5f * footprint) * 0.5f) - 0.5f)) / footprint;
    float pattern = 0.5f - 0.5f * filtered.x * filtered.y;

    float reach = saturate((Range.x - travel) / Range.y);
    float coverage = pattern * reach * step(0.001f, ray.z) * step(0.0f, rise);

    float3 colour = AmbientColour.rgb + SunColour.rgb * Slab.z;
    return float4(colour * Eye.w * coverage, 1.0f - coverage);
}
