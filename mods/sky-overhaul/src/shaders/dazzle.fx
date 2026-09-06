// The dazzle, drawn over the finished frame.
//
// Three entry points, compiled separately by fxc at build time into headers the plugin embeds, so
// the game needs no D3DX and the build needs no DirectX SDK. See CMakeLists.txt.

sampler2D Scene : register(s0);

// The view accumulated over the frames the eye spent dazzled, which is where the smear comes from
// as the view moves.
sampler2D Burn : register(s1);

// How much light fell on each part of the view over those same frames: the bleached region, kept
// apart from the picture so neither has to share a channel with the other.
sampler2D Bleach : register(s2);

// x, y  the sun's place on screen, in texture coordinates
// z     how strongly the eye is dazzled, 0 to 1
// w     how much the contrast is pushed at full dazzle
float4 Sun : register(c0);

// x     how far the glare reaches from the sun, in vertical texture coordinates
// y     the frame's aspect, so the glare stays round
// z     how much of the glare covers the frame wherever the sun happens to be
// w     how much colour drains at full dazzle
float4 Shape : register(c1);

// x     how darkly the bleached core shows
// y     how much of this frame is laid into the burn
// z     how strongly the faint negative surround shows
// w     the brief positive flash as the eye first releases the image
float4 After : register(c2);

// rgb   what colour the bleached core is right now, which drifts as the cones recover
// a     how far the eye's range has compressed while it recovers
float4 Recovery : register(c3);

// x, y  where the bleached core begins and where it becomes solid
// z     how much of the negative's colour survives, nought for grey and one for a full inversion
float4 Curve : register(c4);

// x     how far the bleached region reaches, which is tighter than the glare around it: the eye
//       burns where the sun's own image falls, not across everything the glare washes
float4 Bleaching : register(c5);

// How the haze divides between draining the picture's colour and flattening its contrast.
static const float kHazeGrey = 0.6f;
static const float kHazeFlat = 0.4f;

// Lays the frame into the burn, weighted so that many frames average together.
float4 AccumulatePS(float2 uv : TEXCOORD0) : COLOR0
{
    return float4(tex2D(Scene, uv).rgb, After.y);
}

// Marks out the region the sun's own image burned, without anything having to draw one. Kept by a
// maximum rather than an average, so it holds the worst each part of the view took over the stare.
float4 BleachPS(float2 uv : TEXCOORD0) : COLOR0
{
    float distance = length((uv - Sun.xy) * float2(Shape.y, 1.0f));
    float radial = 1.0f - smoothstep(0.0f, Bleaching.x, distance);

    return float4(saturate(radial * Sun.z).xxx, After.y);
}

float4 MainPS(float2 uv : TEXCOORD0) : COLOR0
{
    float3 colour = tex2D(Scene, uv).rgb;
    float3 grey = float3(0.299f, 0.587f, 0.114f);

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
    colour = lerp(colour, dot(colour, grey).xxx, saturate(Shape.w * Sun.z));

    colour = saturate((colour - 0.5f) * (1.0f + Sun.w * Sun.z) + 0.5f);
    colour = lerp(colour, 1.0f, wash);

    // The eye's range is compressed while it recovers, so everything behind the afterimage loses a
    // little colour and a little contrast. This is what makes it read as an eye that has been
    // overloaded rather than as a shape laid over the picture.
    colour = lerp(colour, dot(colour, grey).xxx, Recovery.a * kHazeGrey);
    colour = lerp(colour, 0.5f, Recovery.a * kHazeFlat);

    float bleached = smoothstep(Curve.x, Curve.y, saturate(tex2D(Bleach, uv).r));
    float3 burn = tex2D(Burn, uv).rgb;

    // The image releases as a brief positive before it inverts.
    colour = lerp(colour, burn, saturate(After.w * bleached));

    // The negative covers everything the eye was looking at, not just the part the sun burned -
    // it is the whole view that was held on the retina. Kept far weaker than the core, because an
    // obvious full-screen inversion is what makes an afterimage look painted on rather than seen.
    //
    // Most of its colour goes with it. The retina holds the shape of what it saw far better than
    // the hue, so a full complementary inversion reads as a photographic negative rather than as
    // something the eye is doing.
    colour = lerp(colour, 1.0f - lerp(dot(burn, grey).xxx, burn, Curve.z), saturate(After.z));

    // The bleached core, dark and tinted towards whichever cones are slowest to recover.
    colour = lerp(colour, Recovery.rgb, saturate(After.x * bleached));

    return float4(colour, 1.0f);
}
