// Being hit: the view darkened from its edges and seen double, laid over the finished frame
// premultiplied.

// xy: how far the second image lies off the first, in screen fractions; z: its share of the view,
// nought to a half.
float4 Double : register(c0);
// The darkening: x how dark, y how far out from the middle it starts, as a share of the way to a
// corner.
float4 Vignette : register(c1);

// A copy of the frame.
sampler2D Frame : register(s0);

// How dark `strength` makes a point `r` of the way to a corner, from `start`: no edge where it
// begins, dark fast past it, and past a strength of one black sooner.
float Dark(float r, float strength, float start) {
    float t = saturate((r - start) / (1.0f - start));
    float s = t * t * (3.0f - 2.0f * t);
    return saturate(strength * s * (2.0f - s));
}

float4 HitPS(float2 uv : TEXCOORD0) : COLOR0 {
    float r = length(uv - 0.5f) * 1.41421356f;
    float seen = 1.0f - Dark(r, Vignette.x, Vignette.y);
    float3 second = tex2D(Frame, uv - Double.xy).rgb;
    return float4(second * Double.z * seen, 1.0f - (1.0f - Double.z) * seen);
}
