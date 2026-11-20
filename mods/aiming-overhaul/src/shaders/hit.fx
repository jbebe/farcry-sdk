// Being hit: the whole view darkened, its edges darker still and creeping in unevenly, and seen
// double, laid over the finished frame premultiplied.

// xy: how far the second image lies off the first, in screen fractions; z: its share of the view,
// nought to a half.
float4 Double : register(c0);
// x: how dark the edges; y: how far out from the middle they start, as a share of the way to a
// corner; z: how dark the whole view; w: how unevenly the edges creep in.
float4 Dark : register(c1);
// x: which uneven edge, a whole number; w: the screen's width over its height.
float4 Edge : register(c2);

// A copy of the frame.
sampler2D Frame : register(s0);

// How dark `strength` makes a point `r` of the way to a corner, from `start`: no edge where it
// begins, dark fast past it, and past a strength of one black sooner.
float Darkened(float r, float strength, float start) {
    float t = saturate((r - start) / (1.0f - start));
    float s = t * t * (3.0f - 2.0f * t);
    return saturate(strength * s * (2.0f - s));
}

float4 HitPS(float2 uv : TEXCOORD0) : COLOR0 {
    // Nought in the middle and one in a corner, along rounded rectangles that follow the screen's
    // edges; 2^-1/4 brings a corner's 2^1/4 down to one. Three waves, turned by the edge's number,
    // push it in and out around the middle.
    float2 d2 = (uv - 0.5f) * (uv - 0.5f) * 4.0f;
    float r = sqrt(sqrt(dot(d2, d2))) * 0.8408964f;
    float2 p = (uv - 0.5f) * float2(Edge.w, 1.0f);
    // The 1e-5 keeps atan2 off nought by nought in the middle.
    float a = atan2(p.y, p.x + 1e-5f);
    float3 turns = Edge.x * float3(2.4f, 3.7f, 5.3f);
    float wave = 0.5f * sin(3.0f * a + turns.x) + 0.3f * sin(5.0f * a + turns.y) +
                 0.2f * sin(9.0f * a + turns.z);
    r *= 1.0f + Dark.w * wave;
    float seen = (1.0f - Darkened(r, Dark.x, Dark.y)) * (1.0f - Dark.z);
    float3 second = tex2D(Frame, uv - Double.xy).rgb;
    return float4(second * Double.z * seen, 1.0f - (1.0f - Double.z) * seen);
}
