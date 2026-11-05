// Being hit: the view darkened from its edges, cut to black and seen double, laid over the finished
// frame premultiplied.

// xy: how far the second image lies off the first, in screen fractions; z: its share of the view,
// nought to a half; w: how black the view is.
float4 Double : register(c0);
// x: how dark the corners are; y: how far out the darkening starts, as a share of the way from the
// middle to a corner.
float4 Vignette : register(c1);

// A copy of the frame.
sampler2D Frame : register(s0);

float4 HitPS(float2 uv : TEXCOORD0) : COLOR0 {
    float r = length(uv - 0.5f) * 1.41421356f;
    // Three quarters dark two thirds of the way on to the corner, and wholly dark there.
    float threeQuarters = lerp(Vignette.y, 1.0f, 0.68f);
    float dark = Vignette.x * (0.75f * saturate((r - Vignette.y) / (threeQuarters - Vignette.y)) +
                               0.25f * saturate((r - threeQuarters) / (1.0f - threeQuarters)));
    float seen = (1.0f - dark) * (1.0f - Double.w);
    float3 second = tex2D(Frame, uv - Double.xy).rgb;
    return float4(second * Double.z * seen, 1.0f - (1.0f - Double.z) * seen);
}
