// What the ambient occlusion and the cloud shadows share: the engine's linear depth, the camera it
// was drawn from, and how the occlusion is tuned. Everything here is relative to the eye.

// The nearest distance, in metres, that counts as geometry rather than nothing.
#define NEAREST_GEOMETRY 0.05f

sampler2D SceneDepth : register(s3);

// xyz: what a texel's red, green and blue are each worth in metres. w: the farthest distance that
// counts as geometry; past it is what the engine clears to where it drew nothing.
float4 DepthDecode : register(c88);
// x, y: half the view's width and height one metre out. z, w: one full-resolution pixel, in texture
// coordinates.
float4 Lens : register(c89);
// The camera's axes, unit length.
float4 CameraRight : register(c90);
float4 CameraUp : register(c91);
float4 CameraForward : register(c92);
// x: the radius occlusion is looked for within, in metres. y: its strength. z: the distance by
// which it has faded out.
float4 Ambient : register(c93);
// x: the cloud shadows' strength. y: how much of the light on the ground is the sun's.
float4 Shade : register(c94);
// xy: the blur's axis, one half-resolution pixel long. zw: one half-resolution pixel, in texture
// coordinates.
float4 Blur : register(c95);

// Metres along the camera's axis to what was drawn at `uv`, or nought where nothing was.
float Metres(float2 uv) {
    float z = dot(tex2Dlod(SceneDepth, float4(uv, 0.0f, 0.0f)).rgb, DepthDecode.xyz);
    return z > NEAREST_GEOMETRY && z < DepthDecode.w ? z : 0.0f;
}

// The top left of the two by two block of full-resolution depth a half-resolution pixel covers.
float2 FullFromHalf(float2 halfPixel) {
    return (halfPixel * 2.0f + 0.5f) * Lens.zw;
}

// The direction through `uv`, one metre along the camera's axis.
float3 ViewRay(float2 uv) {
    return CameraForward.xyz + CameraRight.xyz * ((uv.x * 2.0f - 1.0f) * Lens.x) +
           CameraUp.xyz * ((1.0f - uv.y * 2.0f) * Lens.y);
}

// The surface's normal at `uv`, facing the eye, taken on each axis from whichever neighbour lies
// closer in depth, so a silhouette does not bend it toward what is behind.
float3 Normal(float2 uv, float z) {
    float3 centre = ViewRay(uv) * z;
    float2 across = float2(Lens.z, 0.0f);
    float2 down = float2(0.0f, Lens.w);
    float left = Metres(uv - across);
    float right = Metres(uv + across);
    float up = Metres(uv - down);
    float below = Metres(uv + down);
    float3 dx = abs(right - z) < abs(left - z) ? ViewRay(uv + across) * right - centre
                                               : centre - ViewRay(uv - across) * left;
    float3 dy = abs(below - z) < abs(up - z) ? ViewRay(uv + down) * below - centre
                                             : centre - ViewRay(uv - down) * up;
    float3 normal = normalize(cross(dx, dy));
    return dot(normal, centre) > 0.0f ? -normal : normal;
}

// A different value per pixel, so that what coarse sampling misses lands as fine noise instead of
// as bands. Fixed to the pixel rather than the frame: the game has nothing that would blend a
// moving pattern away, so a moving one would only flicker.
float PixelNoise(float2 screen) {
    return frac(52.9829189f * frac(0.06711056f * screen.x + 0.00583715f * screen.y));
}
