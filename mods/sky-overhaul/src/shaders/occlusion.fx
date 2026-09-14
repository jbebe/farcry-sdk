// Ambient occlusion from the engine's linear depth, blurred at half resolution and multiplied into
// the world together with the cloud shadows. See src/occlusion.cpp for the passes.

#include "occlusion.inc.fx"

#define SAMPLES 12
#define BLUR_RADIUS 3

// The most the occlusion's radius may cover on screen, in full-resolution pixels.
#define MAX_RADIUS_PIXELS 60.0f

// The half-resolution result: red is what the ambient occlusion leaves, alpha what the cloud
// shadows leave.
sampler2D Occlusion : register(s0);

// Points in a hemisphere around +z, more of them close in.
static const float3 KERNEL[SAMPLES] = {
    float3(0.097f, 0.000f, 0.029f),   float3(-0.058f, 0.054f, 0.082f),
    float3(0.011f, -0.127f, 0.056f),  float3(0.058f, 0.075f, 0.149f),
    float3(-0.189f, -0.033f, 0.120f), float3(0.060f, -0.038f, 0.280f),
    float3(-0.071f, 0.265f, 0.239f),  float3(-0.195f, -0.376f, 0.155f),
    float3(0.323f, 0.118f, 0.431f),   float3(-0.542f, 0.224f, 0.311f),
    float3(0.141f, -0.302f, 0.715f),  float3(0.223f, 0.711f, 0.550f),
};

// How far apart two depths may be, in metres at a given distance, and still be one surface to the
// blur and the upsample.
float Tolerance(float z) {
    return 0.1f + 0.05f * z;
}

float4 AmbientPS(float2 screen : VPOS) : COLOR0 {
    float2 uv = FullFromHalf(screen);
    float z = Metres(uv);
    if (z <= 0.0f || z >= Ambient.z) {
        return 1.0f;
    }
    float3 centre = ViewRay(uv) * z;
    float3 normal = Normal(uv, z);

    // Close to the eye a metre covers much of the screen, where the samples would only thin out.
    float pixelsPerMetre = 0.5f / (Lens.y * z * Lens.w);
    float radius = min(Ambient.x, MAX_RADIUS_PIXELS / pixelsPerMetre);

    // The kernel turned about the normal by a different angle per pixel, which the blur averages.
    float angle = PixelNoise(screen) * 6.2831853f;
    float3 tangent = normalize(cross(normal, abs(normal.z) < 0.99f ? float3(0.0f, 0.0f, 1.0f)
                                                                   : float3(1.0f, 0.0f, 0.0f)));
    float3 bitangent = cross(normal, tangent);
    tangent = tangent * cos(angle) + bitangent * sin(angle);
    bitangent = cross(normal, tangent);

    float bias = 0.02f + 0.002f * z;
    float occluded = 0.0f;
    [unroll] for (int i = 0; i < SAMPLES; i++) {
        float3 at = centre + (tangent * KERNEL[i].x + bitangent * KERNEL[i].y +
                              normal * KERNEL[i].z) * radius;
        float depth = max(dot(at, CameraForward.xyz), 0.05f);
        float2 atUv = float2(0.5f + 0.5f * dot(at, CameraRight.xyz) / (depth * Lens.x),
                             0.5f - 0.5f * dot(at, CameraUp.xyz) / (depth * Lens.y));
        float scene = Metres(atUv);

        // Something drawn in front of the sample hides it, unless it stands so far in front that
        // it is another object altogether.
        float inFront = step(0.001f, scene) * step(scene + bias, depth);
        float near = smoothstep(0.0f, 1.0f, radius / max(abs(z - scene), 0.001f));
        occluded += inFront * near;
    }

    float fade = 1.0f - smoothstep(Ambient.z * 0.5f, Ambient.z, z);
    return 1.0f - occluded / SAMPLES * fade;
}

// One axis of a blur that keeps to a surface.
float4 BlurPS(float2 screen : VPOS) : COLOR0 {
    float z = Metres(FullFromHalf(screen));
    float tolerance = Tolerance(z);
    float4 sum = 0.0f;
    float weight = 0.0f;
    [unroll] for (int i = -BLUR_RADIUS; i <= BLUR_RADIUS; i++) {
        float2 at = screen + Blur.xy * i;
        float atZ = Metres(FullFromHalf(at));
        float w = exp(-0.22f * i * i) * exp(-abs(atZ - z) / tolerance);
        sum += tex2Dlod(Occlusion, float4((at + 0.5f) * Blur.zw, 0.0f, 0.0f)) * w;
        weight += w;
    }
    return sum / weight;
}

// Multiplies the world by what the occlusion and the shadows leave, read from the half-resolution
// pixels around this one that lie on the same surface, or from all four alike where none does.
float4 ApplyPS(float2 screen : VPOS) : COLOR0 {
    float z = Metres((screen + 0.5f) * Lens.zw);
    if (z <= 0.0f) {
        return 1.0f;
    }
    float tolerance = Tolerance(z);
    float2 halfPixel = screen * 0.5f;
    float2 base = floor(halfPixel);
    float2 along = halfPixel - base;

    float4 sum = 0.0f;
    float weight = 0.0f;
    [unroll] for (int y = 0; y < 2; y++) {
        [unroll] for (int x = 0; x < 2; x++) {
            float2 at = base + float2(x, y);
            float2 share = lerp(1.0f - along, along, float2(x, y));
            float w = (share.x * share.y + 0.001f) *
                      (exp(-abs(Metres(FullFromHalf(at)) - z) / tolerance) + 0.0001f);
            sum += tex2Dlod(Occlusion, float4((at + 0.5f) * Blur.zw, 0.0f, 0.0f)) * w;
            weight += w;
        }
    }
    float4 kept = sum / weight;
    float factor = lerp(1.0f, kept.r, Ambient.y) * lerp(1.0f, kept.a, Shade.x);
    return float4(factor, factor, factor, 1.0f);
}
