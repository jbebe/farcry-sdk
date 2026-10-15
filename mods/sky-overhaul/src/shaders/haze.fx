// Heat haze and mirage: the world seen through the hot air over sunlit ground.

#include "depth.inc.fx"
#include "hash.inc.fx"
#include "solid.inc.fx"

sampler2D Scene : register(s0);

// x  how far the air bends a look over REFERENCE_PATH of hot air, in radians
// y  how much of the ground a mirage replaces
// z  the sine of the shallowest look onto bare level ground that still shows a mirage
// w  how deep the shimmering air over the ground is, in metres
float4 Heat : register(c96);

// x  seconds, for the shimmer's motion
float4 Time : register(c97);

// xy  what the solid depth is linearised with
float4 Solid : register(c98);

#define PI 3.14159265f

// The path through hot air at which the bend is as strong as Heat.x says, and the most it grows
// past that.
static const float REFERENCE_PATH = 200.0f;
static const float MOST_GROWTH = 4.0f;

// Shimmer cells across one radian of view, sideways and up: taller than wide, as rising air is.
static const float2 CELLS = float2(50.0f, 120.0f);

// How fast the cells rise, in cells a second.
static const float RISE = 1.5f;

// How far below a pixel of sky to look for the land it sits over, in pixels.
static const float SKYLINE_REACH = 12.0f;

// The share of the shimmer that moves a look sideways rather than up and down.
static const float SIDEWAYS = 0.5f;

// The blur's radius as a share of the bend, and the least radius worth blurring, in pixels.
static const float BLUR_SHARE = 0.6f;
static const float LEAST_BLUR = 0.5f;

static const float2 BLUR_TAPS[6] = {
    float2(1.0f, 0.0f), float2(0.5f, 0.866f), float2(-0.5f, 0.866f),
    float2(-1.0f, 0.0f), float2(-0.5f, -0.866f), float2(0.5f, -0.866f)};

// The steepest look down, as a sine, worth testing for a mirage at all.
static const float MIRAGE_SEARCH = 0.12f;

// How far apart the points are, in pixels, that tell level ground from a hill or a rock.
static const float FOOTPRINT = 6.0f;

// The ground's slope, as the up component of its normal, over which a mirage fades out.
static const float2 LEVEL = float2(0.97f, 0.995f);

// How far from the plane through its neighbours, in metres and per metre of distance, a point may
// sit and still be the same level ground.
static const float2 PLANE_TOLERANCE = float2(0.05f, 0.002f);

// The distances over which a mirage comes in, in metres.
static const float2 MIRAGE_DISTANCE = float2(60.0f, 120.0f);

// How high above the ground without grass a look may land, in metres, and still be the ground.
static const float GRASS_HEIGHT = 0.2f;

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

bool IsLand(float stored)
{
    return stored > NEAREST_GEOMETRY && !IsSky(stored);
}

// Whether what was drawn `there` metres out may be pulled over a pixel `z` metres out: never the
// weapon, nor anything nearer.
bool MayCover(float there, float z)
{
    return there > NEAREST_GEOMETRY && (IsSky(there) || there >= z * 0.9f);
}

// The world around `centre`, over a disc `radius` across in texture coordinates, turned by `turn`
// half turns, leaving out whatever may not cover a pixel `z` metres out.
float3 Look(float2 centre, float2 radius, float turn, float z)
{
    float3 sum = tex2Dlod(Scene, float4(centre, 0.0f, 0.0f)).rgb;
    float weight = 1.0f;
    [branch] if (radius.y > LEAST_BLUR * Lens.w)
    {
        float s, c;
        sincos(turn * PI, s, c);
        [unroll] for (int i = 0; i < 6; i++)
        {
            float2 tap = BLUR_TAPS[i];
            float2 at = centre + float2(tap.x * c - tap.y * s, tap.x * s + tap.y * c) * radius;
            float w = MayCover(Stored(at), z) ? 1.0f : 0.0f;
            sum += tex2Dlod(Scene, float4(at, 0.0f, 0.0f)).rgb * w;
            weight += w;
        }
    }
    return sum / weight;
}

// Whether a look at `uv`, `stored` metres out, lands on bare ground rather than on grass or leaves
// standing on it.
bool Bare(float2 uv, float stored, float sine)
{
    float solid = SolidMetresWith(uv, Solid.xy);
    return IsLand(stored) && solid > 0.0f && (solid - stored) * sine < GRASS_HEIGHT;
}

// How much a mirage covers a look at `uv` that lands at `at`, and where the look it mirrors lands.
// Only bare level ground far off has one: not a hill, a rock or grass.
float Mirage(float2 uv, float3 direction, float3 at, float z, float wobble, out float2 mirror)
{
    mirror = uv;
    float2 across = float2(FOOTPRINT * Lens.z, 0.0f);
    float2 down = float2(0.0f, FOOTPRINT * Lens.w);
    float left = Stored(uv - across);
    float right = Stored(uv + across);
    float below = Stored(uv + down);
    float sine = abs(direction.z);
    if (!Bare(uv, z, sine) || !Bare(uv - across, left, sine) || !Bare(uv + across, right, sine) ||
        !Bare(uv + down, below, sine))
    {
        return 0.0f;
    }

    float3 l = ViewRay(uv - across) * left;
    float3 r = ViewRay(uv + across) * right;
    float3 b = ViewRay(uv + down) * below;
    float3 normal = normalize(cross(r - l, b - l));
    normal *= sign(normal.z);
    if (abs(dot(at - l, normal)) > PLANE_TOLERANCE.x + PLANE_TOLERANCE.y * z)
    {
        return 0.0f;
    }

    // The look's angle to the ground itself, not to the horizon.
    float grazing = -dot(direction, normal);
    float edge = Heat.z * (1.0f + 0.4f * wobble);
    float band = grazing > 0.0f ? smoothstep(edge, 0.5f * edge, grazing) : 0.0f;
    float level = smoothstep(LEVEL.x, LEVEL.y, normal.z);
    float distance = smoothstep(MIRAGE_DISTANCE.x, MIRAGE_DISTANCE.y, length(at));

    // Only what lies beyond the ground it would cover: the sky, or land farther off.
    mirror = OnScreen(float3(direction.xy, -direction.z));
    float shown = Stored(mirror);
    bool far = IsSky(shown) || shown > z;
    bool inside = all(mirror == saturate(mirror));
    return far && inside ? Heat.y * band * level * distance : 0.0f;
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

    [branch] if (z <= NEAREST_GEOMETRY)
    {
        return tex2Dlod(Scene, float4(uv, 0.0f, 0.0f));
    }

    float3 at = ray * z;
    float3 direction = normalize(ray);
    float3 shimmer = Shimmer(direction);

    // A look is in the hot air only while it is within its depth of the ground.
    float path = min(length(at), Heat.w / max(abs(direction.z), 1e-3f));
    float bend = Heat.x * min(path / REFERENCE_PATH, MOST_GROWTH);
    float2 toUv = bend / (2.0f * Lens.xy);
    float2 bent = uv + shimmer.xy * float2(SIDEWAYS, 1.0f) * toUv;
    bent = MayCover(Stored(bent), z) ? bent : uv;
    float3 colour = Look(bent, BLUR_SHARE * toUv, shimmer.z, z);

    float depression = -direction.z;
    [branch] if (!sky && Heat.y > 0.0f && depression > 0.0f && depression < MIRAGE_SEARCH)
    {
        float2 mirror;
        float mirage = Mirage(uv, direction, at, z, shimmer.z, mirror);
        [branch] if (mirage > 0.0f)
        {
            float3 mirrored = Look(mirror + (bent - uv), BLUR_SHARE * toUv, shimmer.z, 0.0f);
            colour = lerp(colour, mirrored, mirage);
        }
    }
    return float4(colour, 1.0f);
}
