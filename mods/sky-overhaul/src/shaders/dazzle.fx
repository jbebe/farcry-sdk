// The dazzle, drawn over the finished frame.
//
// Two entry points, compiled separately by fxc at build time into headers the plugin embeds, so
// the game needs no D3DX and the build needs no DirectX SDK. See CMakeLists.txt.

sampler2D Scene : register(s0);

// What the eye has burned in: the view accumulated over the frames it spent dazzled, which is
// where the smear comes from as the view moves.
sampler2D Burn : register(s1);

// x, y  the sun's place on screen, in texture coordinates
// z     how strongly the eye is dazzled, 0 to 1
// w     how much the contrast is pushed at full dazzle
float4 Sun : register(c0);

// x     how far the glare reaches from the sun, in vertical texture coordinates
// y     the frame's aspect, so the glare stays round
// z     how much of the glare covers the frame wherever the sun happens to be
// w     how much colour drains at full dazzle
float4 Shape : register(c1);

// x     how strongly the burned-in view shows once the player looks away
// y     how much of this frame is laid into the burn
float4 After : register(c2);

// Lays the frame into the burn, weighted so that many frames average together.
float4 AccumulatePS(float2 uv : TEXCOORD0) : COLOR0
{
    return float4(tex2D(Scene, uv).rgb, After.y);
}

float4 MainPS(float2 uv : TEXCOORD0) : COLOR0
{
    float3 colour = tex2D(Scene, uv).rgb;

    // Brightest beside the sun and falling away from it, because glare is light scattered inside
    // the eye rather than a uniform veil.
    float distance = length((uv - Sun.xy) * float2(Shape.y, 1.0f));
    float radial = 1.0f - smoothstep(0.0f, Shape.x, distance);

    // A floor under that gradient. Without it the brightness would depend on where the sun sits in
    // the frame rather than on the angle to it, and would fall away as the sun left the edge of
    // the screen even though the eye is still pointed near it.
    float reach = max(radial, Shape.z);

    // Spreading to the whole frame only as the dazzle approaches total is what makes looking
    // straight at the sun wash out to white while a sun off to one side stays a local glare.
    float wash = saturate(Sun.z * lerp(reach, 1.0f, Sun.z * Sun.z));

    // Bleaching drains colour the way a dazzled eye loses hue before it loses shape - and it runs
    // before the contrast, not after. Pushing a colour away from mid grey stretches its chroma
    // along with everything else, so contrast on a coloured picture makes it more colourful, which
    // is the opposite of what heavy sunlight does. On an already grey picture it stays grey.
    float luma = dot(colour, float3(0.299f, 0.587f, 0.114f));
    colour = lerp(colour, luma.xxx, saturate(Shape.w * Sun.z));

    colour = saturate((colour - 0.5f) * (1.0f + Sun.w * Sun.z) + 0.5f);
    colour = lerp(colour, 1.0f, wash);

    // The burn, inverted, laid over what the eye is looking at now. Read at its own size, so what
    // softness it has is the smear it was built with rather than anything added here.
    float3 burn = 1.0f - tex2D(Burn, uv).rgb;
    colour = lerp(colour, burn, After.x);

    return float4(colour, 1.0f);
}
