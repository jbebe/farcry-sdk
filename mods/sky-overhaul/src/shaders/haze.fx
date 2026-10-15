// Heat haze and mirage: the world seen through the hot air over sunlit ground.

#include "depth.inc.fx"
#include "hash.inc.fx"

sampler2D Scene : register(s0);

// x  how far the air bends a look at full heat over REFERENCE_PATH of hot air, in radians
// y  how much of the ground a mirage replaces
// z  the sine of the steepest look onto flat ground that still shows a mirage
// w  how deep the hot air over the ground is, in metres
float4 Heat : register(c96);

// x  seconds, for the shimmer's motion
float4 Time : register(c97);

// The path through hot air at which the shimmer is as strong as Heat.x says.
static const float REFERENCE_PATH = 200.0f;

// Shimmer cells across one radian of view, sideways and up: taller than wide, as rising air is.
static const float2 CELLS = float2(150.0f, 400.0f);

// How fast the cells rise, in cells a second.
static const float RISE = 1.5f;

// How far below a pixel of sky to look for the land it sits over, in pixels.
static const float SKYLINE_REACH = 6.0f;

// The share of the shimmer that moves a look sideways rather than up and down.
static const float SIDEWAYS = 0.5f;

// Smooth noise in [-0.5, 0.5) per channel.
float3 ValueNoise(float3 p)
{
    float3 corner = floor(p);
    float3 f = p - corner;
    f = f * f * (3.0f - 2.0f * f);
    float3 x00 = lerp(Hash(corner), Hash(corner + float3(1, 0, 0)), f.x);
    float3 x10 = lerp(Hash(corner + float3(0, 1, 0)), Hash(corner + float3(1, 1, 0)), f.x);
    float3 x01 = lerp(Hash(corner + float3(0, 0, 1)), Hash(corner + float3(1, 0, 1)), f.x);
    float3 x11 = lerp(Hash(corner + float3(0, 1, 1)), Hash(corner + float3(1, 1, 1)), f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z) - 0.5f;
}

// The air's bend along a world direction, so it stays put in the world as the view turns.
float3 Shimmer(float3 direction)
{
    float3 p = float3(direction.xy * CELLS.x, direction.z * CELLS.y - Time.x * RISE);
    return ValueNoise(p) +
           0.5f * ValueNoise(p * 2.3f + float3(0.0f, Time.x * 0.7f, -Time.x * RISE));
}

float4 MainPS(float2 screen : VPOS) : COLOR0
{
    float2 uv = (screen + 0.5f) * Lens.zw;
    float3 ray = ViewRay(uv);
    float stored = Stored(uv);
    bool sky = IsSky(stored);

    // The sky just over a hot skyline shimmers with the land under it.
    float z = stored;
    [branch] if (sky)
    {
        float below = Stored(uv + float2(0.0f, SKYLINE_REACH * Lens.w));
        z = IsSky(below) ? 0.0f : below;
    }

    // Before any return, since the derivatives need every pixel of the quad.
    float3 at = ray * z;
    float3 normal = cross(ddx(at), ddy(at));
    float flat = smoothstep(0.85f, 0.97f, abs(normal.z) * rsqrt(dot(normal, normal) + 1e-20f));

    [branch] if (z <= NEAREST_GEOMETRY)
    {
        return tex2Dlod(Scene, float4(uv, 0.0f, 0.0f));
    }

    float3 direction = normalize(ray);
    float3 shimmer = Shimmer(direction);

    // A look is in the hot air only while it is within its depth of the ground.
    float path = min(z * length(ray), Heat.w / max(abs(direction.z), 1e-3f));
    float bend = Heat.x * sqrt(path / REFERENCE_PATH);
    float2 offset = shimmer.xy * float2(SIDEWAYS, 1.0f) * bend / (2.0f * Lens.xy);
    float2 bent = uv + offset;

    // Nothing nearer than this pixel is pulled over it, and never the weapon.
    float there = Stored(bent);
    bent = there <= NEAREST_GEOMETRY || (!IsSky(there) && there < z * 0.9f) ? uv : bent;
    float3 colour = tex2Dlod(Scene, float4(bent, 0.0f, 0.0f)).rgb;

    // Flat ground at a grazing look shows the sky mirrored, with a ragged lower edge.
    float depression = -direction.z;
    float edge = Heat.z * (1.0f + 0.4f * shimmer.z);
    float band = depression > 0.0f ? smoothstep(edge, 0.5f * edge, depression) : 0.0f;
    float mirage = sky ? 0.0f : Heat.y * band * flat;
    [branch] if (mirage > 0.0f)
    {
        float2 mirror = OnScreen(float3(direction.xy, depression)) + offset;
        // Only what lies beyond the ground it would cover: the sky, or land farther off.
        float shown = Stored(mirror);
        bool far = IsSky(shown) || shown > z;
        bool inside = all(mirror == saturate(mirror));
        mirage = far && inside ? mirage : 0.0f;
        colour = lerp(colour, tex2Dlod(Scene, float4(mirror, 0.0f, 0.0f)).rgb, mirage);
    }
    return float4(colour, 1.0f);
}
