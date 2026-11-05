// Dirt in the eye: black, see-through blobs with the view blurred around each, laid over the
// finished frame premultiplied, so one draw both blurs and darkens.

// x: the screen's width over its height.
float4 Screen : register(c0);
// Each blob: xy its centre in screen fractions, y down; z its radius in screen heights; w its
// height over its width.
float4 Blobs[4] : register(c1);
// How dark each blob is at its middle, and how much it blurs the view around it, nought to one.
float4 Darkness : register(c5);
float4 Blur : register(c6);

// The frame, blurred.
sampler2D Blurred : register(s0);

float4 DirtPS(float2 uv : TEXCOORD0) : COLOR0 {
    float dark = 0.0f;
    float blur = 0.0f;
    for (int i = 0; i < 4; i++) {
        float2 at = (uv - Blobs[i].xy) * float2(Screen.x, 1.0f / Blobs[i].w);
        float r = length(at) / max(Blobs[i].z, 0.0001f);
        // Dark in its middle and gone past its edge; the view blurred out to 2.4 times as far.
        dark = max(dark, Darkness[i] * (1.0f - smoothstep(0.1f, 1.1f, r)));
        blur = max(blur, Blur[i] * (1.0f - smoothstep(1.08f, 2.4f, r)));
    }
    float3 blurred = tex2D(Blurred, uv).rgb;
    return float4(blurred * blur * (1.0f - dark), 1.0f - (1.0f - blur) * (1.0f - dark));
}
