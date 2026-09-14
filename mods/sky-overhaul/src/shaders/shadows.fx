// The cloud shadows, drawn by ShadowPS in clouds.fx at half resolution, blurred and multiplied into
// the world. See src/shadows.cpp for the passes.

#include "shadows.inc.fx"

#define BLUR_RADIUS 3

// The half-resolution result: alpha is what the clouds leave of the sunlight.
sampler2D Shadows : register(s0);

// How far apart two depths may be, in metres at a given distance, and still be one surface to the
// blur and the upsample.
float Tolerance(float z) {
    return 0.1f + 0.05f * z;
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
        sum += tex2Dlod(Shadows, float4((at + 0.5f) * Blur.zw, 0.0f, 0.0f)) * w;
        weight += w;
    }
    return sum / weight;
}

// Multiplies the world by what the shadows leave, read from the half-resolution pixels around this
// one that lie on the same surface, or from all four alike where none does.
float4 ApplyPS(float2 screen : VPOS) : COLOR0 {
    float z = Metres((screen + 0.5f) * Lens.zw);
    if (z <= 0.0f) {
        return 1.0f;
    }
    float tolerance = Tolerance(z);
    float2 halfPixel = screen * 0.5f;
    float2 base = floor(halfPixel);
    float2 along = halfPixel - base;

    float sum = 0.0f;
    float weight = 0.0f;
    [unroll] for (int y = 0; y < 2; y++) {
        [unroll] for (int x = 0; x < 2; x++) {
            float2 at = base + float2(x, y);
            float2 share = lerp(1.0f - along, along, float2(x, y));
            float w = (share.x * share.y + 0.001f) *
                      (exp(-abs(Metres(FullFromHalf(at)) - z) / tolerance) + 0.0001f);
            sum += tex2Dlod(Shadows, float4((at + 0.5f) * Blur.zw, 0.0f, 0.0f)).a * w;
            weight += w;
        }
    }
    float factor = lerp(1.0f, sum / weight, Shade.x);
    return float4(factor, factor, factor, 1.0f);
}
