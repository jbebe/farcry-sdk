// Film grain, drawn over the finished frame under the heads-up display.

sampler2D Scene : register(s0);

// xy  the frame's size in pixels
// z   one over how wide one grain is, in pixels
// w   which of the grain's patterns shows this moment
float4 Frame : register(c0);

// x  the grain's strength at mid grey
// y  how far each channel's grain differs from the others', nought for grey grain
float4 Look : register(c1);

static const float3 LUMA = float3(0.299f, 0.587f, 0.114f);

// Three values in [0, 1) per lattice point and moment. No sine, which loses precision at the large
// arguments a lattice across the screen gives it.
float3 Hash(float3 p)
{
    p = frac(p * float3(0.1031f, 0.1030f, 0.0973f));
    p += dot(p, p.yxz + 33.33f);
    return frac((p.xxy + p.yxx) * p.zyx);
}

float4 MainPS(float2 uv : TEXCOORD0) : COLOR0
{
    float3 colour = tex2D(Scene, uv).rgb;

    // Value noise on a lattice one grain wide, whose points sit on pixel centres at one pixel.
    float2 cell = (uv * Frame.xy - 0.5f) * Frame.z;
    float2 corner = floor(cell);
    float2 f = cell - corner;
    f = f * f * (3.0f - 2.0f * f);
    float3 a = Hash(float3(corner, Frame.w));
    float3 b = Hash(float3(corner + float2(1.0f, 0.0f), Frame.w));
    float3 c = Hash(float3(corner + float2(0.0f, 1.0f), Frame.w));
    float3 d = Hash(float3(corner + float2(1.0f, 1.0f), Frame.w));
    float3 noise = lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y) - 0.5f;
    noise = lerp(noise.xxx, noise, Look.y);

    // Strongest in the mid tones and gone at black and white, as on a print.
    float luminance = saturate(dot(colour, LUMA));
    float response = sqrt(4.0f * luminance * (1.0f - luminance));
    return float4(colour + noise * Look.x * response, 1.0f);
}
