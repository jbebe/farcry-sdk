// The clouds, drawn over the world's own sky pass in place of the engine's.
//
// The output convention is the engine's own cloud layer's, so that the bloom and tone mapping
// after it see what they saw before: colour premultiplied and scaled by the exposure, alpha the
// transmittance, blended with (one, source alpha).

// xyz: where the camera is, in world space with Z up. w: the frame's exposure.
float4 Eye : register(c71);

// x: how far the slab sits above the camera. y: how deep it is. z: how much of it is filled.
// w: how wide one cell of the test pattern is.
float4 Slab : register(c72);

// The light the engine would have lit its own clouds by, this frame.
float4 SunColour : register(c73);
float4 AmbientColour : register(c74);

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
float4 MainPS(float3 rayIn : TEXCOORD0) : COLOR0 {
    const float4 clear = float4(0.0f, 0.0f, 0.0f, 1.0f);

    float3 ray = normalize(rayIn);
    if (ray.z <= 0.001f) {
        return clear;
    }

    float3 hit = Eye.xyz + ray * (Slab.x / ray.z);

    float2 cell = floor(hit.xy / Slab.w);
    if (frac((cell.x + cell.y) * 0.5f) < 0.25f) {
        return clear;
    }

    float3 colour = AmbientColour.rgb + SunColour.rgb * Slab.z;
    return float4(colour * Eye.w, 0.0f);
}
