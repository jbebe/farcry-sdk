// The player's health in the picture: a soft glow laid over the frame, its colour drained toward
// grey, and grain over it, drawn opaque over the finished frame.

// x: the colour left, nought to one; y: the grain's strength; z: the glow's; w: this frame's seed.
float4 Look : register(c0);
// xy: the grain's cells across and down the screen.
float4 Cells : register(c1);

// A copy of the frame, and the frame blurred wide.
sampler2D Frame : register(s0);
sampler2D Soft : register(s1);

float4 HealthPS(float2 uv : TEXCOORD0) : COLOR0 {
    float3 colour = tex2D(Frame, uv).rgb;
    // Screened over itself, the blur lifts the image toward its own soft light.
    float3 soft = tex2D(Soft, uv).rgb;
    colour = 1.0f - (1.0f - colour) * (1.0f - soft * Look.z);
    float grey = dot(colour, float3(0.299f, 0.587f, 0.114f));
    colour = lerp(grey.xxx, colour, Look.x);
    // A hash without sine, even across the screen: sine's precision falls off with large cells and
    // leaves stripes.
    float2 cell = floor(uv * Cells.xy) + Look.w;
    float3 p = frac(cell.xyx * 0.1031f);
    p += dot(p, p.yzx + 33.33f);
    float noise = frac((p.x + p.y) * p.z) - 0.5f;
    return float4(saturate(colour + noise * Look.y), 1.0f);
}
