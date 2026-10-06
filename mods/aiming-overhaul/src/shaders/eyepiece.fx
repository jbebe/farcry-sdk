// The scope laid over the finished frame: its reticle, from the texture the engine draws it with,
// and its body, black and soft. See src/eyepiece.cpp.

// rgb: the colour the reticle's texture is drawn in. w: one for a piece the engine alpha-tests.
float4 Look : register(c0);
// x: the screen's width over its height. yz: the lens's centre off the screen's, y down. w: its
// radius. All in screen heights.
float4 Lens : register(c1);
// The body, in lens radii: x its opening's radius as drawn, y its outer edge's, z the mount's
// half-width, w its softness.
float4 Body : register(c2);
// xy: the look's centre off the lens's, in lens radii, y down. z: the shape field's width and w the
// distances its bytes span, in lens radii; z is nought for a scope with no shape of its own.
float4 Shape : register(c3);

sampler2D Reticle : register(s0);
// The eyepiece's shape as a distance field; see src/scopes.h.
sampler2D ShapeField : register(s1);

// Within the opening only, where the engine's housing would have hidden the rest; `at` is where
// the point is from the lens's centre, in lens radii. Wherever the engine's alpha test passes, at
// least, the piece is wholly covered; below that its cover fades, so that its edges are smooth and
// its thin lines kept as the texture is minified.
float4 ReticlePS(float2 uv : TEXCOORD0, float2 at : TEXCOORD1) : COLOR0 {
    clip(1.0f - length(at));
    float4 texel = tex2D(Reticle, uv);
    float cover = lerp(texel.a, saturate(2.0f * texel.a), Look.w);
    return float4(texel.rgb * Look.rgb, cover);
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
