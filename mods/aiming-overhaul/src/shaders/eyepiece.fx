// The scope laid over the finished frame: its reticle, from the scope's image, and its body, black
// and soft. See src/eyepiece.cpp.

// An illuminated reticle's light: x how much it spreads around its strokes, y how far their insides
// burn toward a hotter shade of their colour. Both nought for a printed one.
float4 Light : register(c0);
// x: the screen's width over its height. yz: the lens's centre off the screen's, y down. w: its
// radius. All in screen heights.
float4 Lens : register(c1);
// The body, in lens radii: x its opening's radius as drawn, y its outer edge's, z the mount's
// half-width, w its softness.
float4 Body : register(c2);
// xy: the look's centre off the lens's, in lens radii, y down. z: the shape field's width and w the
// distances its bytes span, in lens radii; z is nought for a scope with no shape of its own.
float4 Shape : register(c3);

// Colour multiplied by cover, with its mip chain.
sampler2D Reticle : register(s0);
// The eyepiece's shape as a distance field; see src/scopes.h.
sampler2D ShapeField : register(s1);

// Within the opening only; `at` is the point from the lens's centre, in lens radii. A stroke's
// inside is where it still covers wholly blurred over a couple of pixels; it burns brighter than
// its edge, past its colour toward white. Its light is its own colour blurred wide, added where
// nothing is covered too.
float4 ReticlePS(float2 uv : TEXCOORD0, float2 at : TEXCOORD1) : COLOR0 {
    clip(1.0f - length(at));
    float4 texel = tex2D(Reticle, uv);
    float inside = smoothstep(0.5f, 1.0f, tex2Dbias(Reticle, float4(uv, 0.0f, 1.0f)).a);
    float3 hot = saturate(texel.rgb * 2.0f + 0.2f * texel.a);
    float3 halo = tex2Dbias(Reticle, float4(uv, 0.0f, 2.5f)).rgb +
                  tex2Dbias(Reticle, float4(uv, 0.0f, 4.0f)).rgb;
    return float4(lerp(texel.rgb, hot, inside * Light.y) + halo * Light.x, texel.a);
}

// Black over the scope's body around the lens: a ring around the opening, a mount from its centre
// down past the screen's foot, and the eyepiece's own shape, each with a soft edge.
float4 BodyPS(float2 uv : TEXCOORD0) : COLOR0 {
    float2 at = ((uv - 0.5f) * float2(Lens.x, 1.0f) - Lens.yz) / Lens.w;
    float distance = length(at);
    float ring = smoothstep(-Body.w, Body.w, distance - Body.x) *
                 (1.0f - smoothstep(-Body.w, Body.w, min(distance - Body.y,
                                                         max(abs(at.x) - Body.z, -at.y))));
    float2 field = (at - Shape.xy) / max(Shape.z, 0.001f) + 0.5f;
    float inside = (tex2D(ShapeField, field).r - 0.5f) * Shape.w;
    float ink = max(ring, step(0.001f, Shape.z) * smoothstep(-Body.w, Body.w, inside));
    clip(ink - 0.002f);
    return float4(0.0f, 0.0f, 0.0f, ink);
}
