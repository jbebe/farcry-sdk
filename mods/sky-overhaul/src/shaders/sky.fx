// The sky, drawn in place of the engine's own dome.
//
// The clouds are drawn over a finished pass; this is drawn inside one, where the dome's own draw
// was dropped. So it inherits the dome's state rather than setting any: depth tested against the
// world and not written, no culling, and blended source alpha over one minus source alpha. That
// blend is straight rather than premultiplied - the opposite of the cloud layer's convention - so
// the colour here is the sky's own and the alpha is how much of it covers what was drawn behind.
//
// Which matters most at night. The stars and the moon are drawn before the dome, not after, and it
// is the dome's alpha that lets them through: opaque by day, and by night only as bright as the
// sky itself is.

// xyz: where the camera is, in world space with Z up. w: the frame's exposure.
float4 Eye : register(c71);
// xyz: the direction of the sun, pointing at it. w: zero in daylight, one at night.
float4 Sun : register(c72);

struct VertexIn {
    float4 position : POSITION0;
    float3 ray : TEXCOORD0;
};

struct VertexOut {
    float4 position : POSITION0;
    float3 ray : TEXCOORD0;
};

// The quad arrives in clip space already, carrying the world-space ray through each corner, so
// there is nothing to transform and no vertex constant to depend on.
VertexOut MainVS(VertexIn input) {
    VertexOut output;
    output.position = input.position;
    output.ray = input.ray;
    return output;
}

float4 MainPS(VertexOut input) : COLOR {
    float3 ray = normalize(input.ray);

    // Deliberately nothing like a sky. This build exists to show that the substitution happened at
    // all and that everything the engine draws after the dome still lands on top of it, so the one
    // thing the colour has to be is unmistakable.
    float height = saturate(ray.z);
    float3 colour = lerp(float3(0.80f, 0.05f, 0.50f), float3(0.05f, 0.70f, 0.25f), height);

    // The dome's own alpha, taken before the exposure as the dome takes it.
    float luminance = dot(colour, float3(0.299f, 0.587f, 0.114f));
    float alpha = saturate((1.0f - Sun.w) + luminance * 0.5f);

    return float4(colour * Eye.w, alpha);
}
