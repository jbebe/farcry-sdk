// The world around the scope at the eye's own field of view, laid down outside the eyepiece's
// opening before the gun is drawn over it.

// x: the screen's width over its height. y: the smallest radius taken for a lens, in screen heights.
float4 Screen : register(c0);

// The second view, at the eye's own field of view.
sampler2D Surroundings : register(s0);
// x: the opening's radius. yz: its centre off the screen's, y down. All in screen heights.
sampler2D Lens : register(s5);

float4 SurroundingsPS(float2 uv : TEXCOORD0) : COLOR0 {
    float3 lens = tex2D(Lens, float2(0.5f, 0.5f)).xyz;
    float2 at = (uv - 0.5f) * float2(Screen.x, 1.0f) - lens.yz;
    clip(length(at) - lens.x);
    clip(lens.x - Screen.y);
    return tex2D(Surroundings, uv);
}
