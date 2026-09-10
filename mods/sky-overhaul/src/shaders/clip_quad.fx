// The vertex shader DrawGuard::ClipQuad draws through. The quad arrives in clip space already,
// carrying the world-space ray through each corner, so there is nothing to transform.

struct RayVertex {
    float4 position : POSITION0;
    float3 ray : TEXCOORD0;
};

RayVertex MainVS(RayVertex input) {
    return input;
}
