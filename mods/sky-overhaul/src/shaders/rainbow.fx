// The rainbow: sunlight bent back out of the raindrops round the point opposite the sun, added over
// the sky and the clouds. See src/rainbow.cpp.

#include "hash.inc.fx"
#include "horizon.inc.fx"

// Degrees from the point opposite the sun where each colour is brightest, red outermost, and how
// wide it falls off outside and inside that ring: the outer edge is sharp, the colours bleed inward.
#define PEAK float3(42.2f, 41.5f, 40.9f)
#define OUTSIDE 0.3f
#define INSIDE 0.75f

// The white light inside the bow, brightest against it and gone by GLOW_GONE degrees.
#define GLOW 0.12f
#define GLOW_GONE 25.0f

// How much red the blue ring carries, which turns the bow's violet edge violet.
#define VIOLET_RED 0.15f

// Over how much of the arc, centred on the cut, the bow's cut end fades out.
#define CUT_FADE 0.12f

// x: how bright the bow's brightest colour is against the sunlight, at the bow's strength and the
// frame's exposure.
float4 Light : register(c71);
// xyz: the direction of the sun, pointing at it.
float4 Sun : register(c72);
// xy: the filled land under the horizon, as horizon.inc.fx takes it.
float4 Fill : register(c73);
// x: which foot the bow is kept from, one way round the ring or the other; y: how much of its arc
// over the horizon it keeps from there; z: the angle round the ring from its top to either foot.
float4 Cut : register(c74);

// The texel of what the clouds let through of the sun to the eye.
sampler2D Sunlit : register(s0);

// Each colour's ring across `degrees` from its peak.
float3 Ring(float degrees) {
    float3 off = degrees - PEAK;
    float3 x = off / (off > 0.0f ? OUTSIDE : INSIDE);
    return exp(-0.5f * x * x);
}

// How much of the bow stands along a ray: all of it from the kept foot, fading out where it is cut.
float Kept(float3 ray, float3 away) {
    float3 up = normalize(float3(0.0f, 0.0f, 1.0f) - away * away.z);
    float around = atan2(dot(ray, cross(away, up)), dot(ray, up));
    float along = (Cut.x * around + Cut.z) / (2.0f * Cut.z);
    return 1.0f - smoothstep(Cut.y - 0.5f * CUT_FADE, Cut.y + 0.5f * CUT_FADE, along);
}

float4 MainPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    float3 ray = normalize(rayIn);
    float degrees = acos(clamp(-dot(ray, Sun.xyz), -1.0f, 1.0f)) * 57.29578f;

    float3 bow = Ring(degrees);
    bow.r += VIOLET_RED * bow.b;
    bow += degrees < PEAK.b ? GLOW * smoothstep(GLOW_GONE, PEAK.b, degrees) : 0.0f;

    float3 sunlit = tex2Dlod(Sunlit, float4(0.5f, 0.5f, 0.0f, 0.0f)).rgb;
    float shown = Light.x * Kept(ray, -Sun.xyz) * (1.0f - Filled(ray, Fill.xy));
    float3 colour = bow * sunlit * shown;
    // Its gradients broken up as the sky's are, only where it adds anything.
    float dither = (Dither(screen) - 0.5f) / 255.0f * saturate(dot(colour, 64.0f));
    return float4(colour + dither, 0.0f);
}
