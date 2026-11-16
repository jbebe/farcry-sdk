// The world around the scope at the eye's own field of view, laid down outside the eyepiece's
// opening before the gun is drawn over it, fading from sharp to soft as the eyepiece opens.

// x: the screen's width over its height. yz: the opening's centre off the screen's, y down. w: its
// radius. All in screen heights.
float4 Opening : register(c0);
// x: how far the surroundings have softened, nought to one.
float4 Soften : register(c1);

// The second view at the eye's own field of view, softened, then as it is.
sampler2D Soft : register(s0);
sampler2D Sharp : register(s1);

float4 SurroundingsPS(float2 uv : TEXCOORD0) : COLOR0 {
    float2 at = (uv - 0.5f) * float2(Opening.x, 1.0f) - Opening.yz;
    clip(length(at) - Opening.w);
    return lerp(tex2D(Sharp, uv), tex2D(Soft, uv), Soften.x);
}
