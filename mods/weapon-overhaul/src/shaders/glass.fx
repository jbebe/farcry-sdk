// The scope's view as seen through its glass, redrawn over the finished frame inside the opening:
// pincushion distortion, colour fringing, a darkening rim, the coating's tint, and smudges that
// catch the light. See src/eyepiece.cpp.

// At the lens's rim, growing toward it from the centre: x how far the view is pulled in, y how far
// red and blue part from green, z how much it darkens, w how brightly the smudges glow.
float4 Glass : register(c0);
// x: the screen's width over its height. yz: the lens's centre off the screen's, y down. w: its
// radius. All in screen heights.
float4 Lens : register(c1);
// rgb: what the coating lets through.
float4 Tint : register(c2);

// The finished frame, copied.
sampler2D Frame : register(s0);
// How smudged the glass is across the lens, from its left edge to its right and top to bottom.
sampler2D Dirt : register(s2);

float4 GlassPS(float2 uv : TEXCOORD0) : COLOR0 {
    float2 aspect = float2(Lens.x, 1.0f);
    float2 at = ((uv - 0.5f) * aspect - Lens.yz) / Lens.w;
    float rim = dot(at, at);
    clip(1.0f - rim);

    // A magnifying eyepiece shows the rim of the view from nearer its centre; red reaches a little
    // wider than green and blue a little narrower.
    float2 seen = at / (1.0f + Glass.x * rim);
    float fringe = Glass.y * rim;
    float2 centre = 0.5f + Lens.yz / aspect;
    float2 toUv = Lens.w / aspect;
    float3 colour = float3(tex2D(Frame, centre + seen * (1.0f + fringe) * toUv).r,
                           tex2D(Frame, centre + seen * toUv).g,
                           tex2D(Frame, centre + seen * (1.0f - fringe) * toUv).b);

    colour *= (1.0f - Glass.z * smoothstep(0.3f, 1.0f, rim)) * Tint.rgb;
    float light = dot(colour, float3(0.299f, 0.587f, 0.114f));
    colour += tex2D(Dirt, at * 0.5f + 0.5f).r * Glass.w * light;
    return float4(colour, 1.0f);
}
