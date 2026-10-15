// The world's hardware depth without its grass and leaves, at half resolution: src/engine/solid_depth.h.

sampler2D SolidDepth : register(s1);

// Metres along the camera's axis to the solid depth at `uv`, or nought where nothing was drawn, from
// the two values SolidDepth::Projection gives.
float SolidMetresWith(float2 uv, float2 projection) {
    float stored = tex2Dlod(SolidDepth, float4(uv, 0.0f, 0.0f)).r;
    return stored < 1.0f ? abs(projection.y / (stored - projection.x)) : 0.0f;
}
