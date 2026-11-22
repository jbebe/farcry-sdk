// Rain drops round the player: each a streak over one exposure of its fall, seen from the eye,
// hidden by the world through the depth test and under cover through the engine's rain cover map.
// Positions are relative to the eye, so they stay precise anywhere in the world.

// Eye-relative world to clip, and to the cover map's clip.
row_major float4x4 ViewProjection : register(c230);
row_major float4x4 CoverTransform : register(c234);
// xyz: how far the drops' lattice has moved, as a fraction of the box; w: the box's size in metres.
float4 Box : register(c238);
// xyz: the streak, metres from head back to tail; w: the share of a box edge the drops fade over.
float4 Streak : register(c239);
// xy: half the viewport in pixels; z: the narrowest a streak is drawn, in pixels; w: the drops'
// width in metres.
float4 Screen : register(c240);
// x: how opaque a drop at its full width is; y: how far drops are drawn, in metres; z: the
// projection's vertical scale.
float4 Look : register(c241);

// rgb: the drops' colour; w: half a texel of the cover map.
float4 DropColour : register(c91);

sampler2D CoverMap : register(s0);

struct DropIn {
    // xyz: where in the box; w: the drop's own size and brightness.
    float4 seed : POSITION0;
    // 0 and 1 at the tail, 2 and 3 at the head, even on the left.
    float corner : TEXCOORD1;
};

struct DropOut {
    float4 position : POSITION;
    float3 cover : TEXCOORD0;
    // x: across the streak, -1 to 1; y: along it, tail 0 to head 1.
    float2 shape : TEXCOORD1;
    float alpha : TEXCOORD2;
};

DropOut DropsVS(DropIn input) {
    DropOut output;
    const float size = Box.w;
    const float3 head = (frac(input.seed.xyz + Box.xyz) - 0.5f) * size;
    const float3 tail = head - Streak.xyz * lerp(0.7f, 1.3f, input.seed.w);

    // Faded toward the box's faces, where drops leave and come in.
    const float3 edge = abs(head) / (0.5f * size);
    const float fade = 1.0f - smoothstep(1.0f - Streak.w, 1.0f, max(max(edge.x, edge.y), edge.z));

    const float4 h = mul(ViewProjection, float4(head, 1.0f));
    const float4 t = mul(ViewProjection, float4(tail, 1.0f));
    const float atHead = step(1.5f, input.corner);
    const float side = (input.corner - 2.0f * floor(0.5f * input.corner)) * 2.0f - 1.0f;

    output.cover = mul(CoverTransform, float4(lerp(tail, head, atHead), 1.0f)).xyz;
    output.shape = float2(side, atHead);

    // Too near to draw: the whole quad goes behind the near plane.
    const float near = step(0.15f, min(h.w, t.w));
    const float headW = max(h.w, 0.15f);
    const float tailW = max(t.w, 0.15f);

    const float2 headPixels = h.xy / headW * Screen.xy;
    const float2 tailPixels = t.xy / tailW * Screen.xy;
    const float2 axis = headPixels - tailPixels;
    const float span = length(axis);
    const float2 along = axis / max(span, 1e-4f);
    const float2 across = float2(-along.y, along.x);

    // A drop thinner than a pixel is drawn a pixel wide and that much fainter.
    const float pixelsPerMetre = Screen.y * Look.z / headW;
    const float width = Screen.w * lerp(0.6f, 1.4f, input.seed.w) * pixelsPerMetre;
    const float drawn = max(width, Screen.z);

    const float2 end = lerp(tailPixels - along * 0.5f * drawn, headPixels + along * 0.5f * drawn, atHead);
    const float2 pixels = end + across * side * 0.5f * drawn;
    const float4 clip = lerp(float4(t.xy, t.z, tailW), float4(h.xy, h.z, headW), atHead);
    // Behind the near plane, the whole quad collapses where nothing is drawn.
    output.position = lerp(float4(0.0f, 0.0f, -1.0f, 1.0f),
                           float4(pixels / Screen.xy * clip.w, clip.z, clip.w), near);

    const float far = saturate(1.0f - headW / Look.y);
    output.alpha = near * Look.x * (width / drawn) * fade * far * lerp(0.6f, 1.2f, input.seed.w);
    return output;
}

float4 DropsPS(DropOut input) : COLOR {
    const float2 uv = input.cover.xy * float2(0.5f, -0.5f) + 0.5f + DropColour.w;
    const float open = tex2Dproj(CoverMap, float4(uv, input.cover.z, 1.0f)).r;
    const float profile = 1.0f - input.shape.x * input.shape.x;
    const float taper = smoothstep(0.0f, 0.3f, input.shape.y) * smoothstep(1.0f, 0.75f, input.shape.y);
    return float4(DropColour.rgb, input.alpha * profile * taper * open);
}
