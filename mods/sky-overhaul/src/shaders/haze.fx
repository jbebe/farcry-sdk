// Heat haze and mirage: the world seen through the hot air over sunlit ground. See src/haze.cpp for
// the passes.

#include "depth.inc.fx"
#include "hash.inc.fx"
#include "solid.inc.fx"

// The world in MainPS and DownPS; what is being blurred in BlurPS.
sampler2D Scene : register(s0);
// Where a mirage shows, blurred, at half resolution: red is how much.
sampler2D Mask : register(s2);
// The far world blurred, at quarter resolution, its colour multiplied by alpha: how much of what
// went into it was far.
sampler2D Blurred : register(s4);

// x  how far the air bends a look over REFERENCE_PATH of hot air, in radians
// y  how much of the ground a mirage replaces
// z  the sine of the steepest look onto bare level ground that still shows a mirage
// w  how deep the shimmering air over the ground is, in metres
float4 Heat : register(c96);

// x  seconds, for the shimmer's motion
// y  how far the view is zoomed in, against the widest it has been
// z  shimmer cells all the way round the horizon, unzoomed, so the pattern meets itself behind the
//    eye
// w  shimmer cells across one radian of height, unzoomed
float4 Time : register(c97);

// xy  what the solid depth is linearised with
// zw  one pixel of the mirage's mask, in texture coordinates
float4 Solid : register(c98);

// x  the up component of the steepest ground's normal that still shows a mirage
// y  the same where it shows whole
// zw the distances over which a mirage comes in, in metres
float4 Level : register(c99);

// x  the distance at which the far world starts to blur, in metres
// y  the distance at which it is wholly blurred
// z  how far it blurs at the most
float4 Far : register(c100);

// xy  the axis of a blur pass, one pixel of its target long
// zw  one pixel of that target, in texture coordinates
float4 Axis : register(c101);

#define PI 3.14159265f

// The path through hot air at which the bend is as strong as Heat.x says, and the most it grows
// past that.
static const float REFERENCE_PATH = 200.0f;
static const float MOST_GROWTH = 4.0f;

// What the shimmer's one layer is scaled by, so it reaches as far as two layers did.
static const float SHIMMER_GAIN = 1.6f;

// How fast the shimmer drifts sideways, in cells a second, and how fast it churns in place.
static const float DRIFT = 3.0f;
static const float CHURN = 4.0f;

// How far below a pixel of sky to look for the land it sits over, in pixels.
static const float SKYLINE_REACH = 12.0f;

// The share of the shimmer that moves a look sideways rather than up and down.
static const float SIDEWAYS = 0.5f;

// The blur's radius as a share of the bend, and the least radius worth blurring, in pixels.
static const float BLUR_SHARE = 0.6f;
static const float LEAST_BLUR = 0.5f;

// The least blur of a mirage's own picture, in pixels.
static const float MIRAGE_BLUR = 2.0f;

// The blur's taps beside the centre, laid on a spiral by the golden angle so they cover the disc.
#define BLUR_TAPS 6
static const float GOLDEN_ANGLE = 2.39996323f;

// How far the ragged edge of a mirage reaches past Heat.z, as a share of it.
static const float RAGGED = 0.4f;

// How far apart the points are, in pixels, that tell level ground from a hill or a rock.
static const float FOOTPRINT = 6.0f;

// How far from the plane through its neighbours, in metres and per metre of distance, a point may
// sit and still be the same level ground.
static const float2 PLANE_TOLERANCE = float2(0.05f, 0.002f);

// How high above the ground without grass a look may land, in metres, and still be the ground.
static const float GRASS_HEIGHT = 0.2f;

// The blurred mask's values over which a mirage comes in: below half of a whole mirage nearby, so
// it fills small gaps and reaches a little past its edge.
static const float2 SPREAD = float2(0.1f, 0.5f);

// A blur pass's half width, in pixels of its target, and its weights from the centre out.
#define BLUR_RADIUS 4
static const float BLUR_WEIGHTS[BLUR_RADIUS + 1] = {1.0f, 0.88f, 0.61f, 0.32f, 0.14f};

// The distance from which things go into the far world's blur at all, as a share of Far.x.
static const float NEAR_SHARE = 0.5f;

// Noise in [-0.5, 0.5) per channel, flat within each cell and quick between them, repeating every
// `period` along x.
float3 ValueNoise(float3 p, float period)
{
    float3 corner = floor(p);
    float3 f = p - corner;
    f = f * f * (3.0f - 2.0f * f);
    f = f * f * (3.0f - 2.0f * f);
    float x0 = corner.x - period * floor(corner.x / period);
    float x1 = x0 + 1.0f < period ? x0 + 1.0f : 0.0f;
    float3 a = float3(x0, corner.yz);
    float3 b = float3(x1, corner.yz);
    float3 x00 = lerp(Hash(a), Hash(b), f.x);
    float3 x10 = lerp(Hash(a + float3(0, 1, 0)), Hash(b + float3(0, 1, 0)), f.x);
    float3 x01 = lerp(Hash(a + float3(0, 0, 1)), Hash(b + float3(0, 0, 1)), f.x);
    float3 x11 = lerp(Hash(a + float3(0, 1, 1)), Hash(b + float3(0, 1, 1)), f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z) - 0.5f;
}

// The air's bend along a world direction, laid round the horizon so it stays put in the world as
// the view turns, drifting sideways and churning. Its cells keep their size on screen at any zoom.
float3 Shimmer(float3 direction)
{
    float period = round(Time.z * Time.y);
    float around = atan2(direction.y, direction.x) * (period / (2.0f * PI));
    float3 p = float3(around - Time.x * DRIFT, direction.z * Time.w * Time.y, Time.x * CHURN);
    return SHIMMER_GAIN * ValueNoise(p, period);
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

// The world around `centre`, over a disc `radius` across in texture coordinates, leaving out
// whatever may not cover a pixel `z` metres out. The taps turn by `turn`, a different share of a
// turn per pixel, so a wide blur lands as grain rather than as copies.
float3 Look(float2 centre, float2 radius, float turn, float z)
{
    float3 sum = tex2Dlod(Scene, float4(centre, 0.0f, 0.0f)).rgb;
    float weight = 1.0f;
    [branch] if (radius.y > LEAST_BLUR * Lens.w)
    {
        [unroll] for (int i = 0; i < BLUR_TAPS; i++)
        {
            float s, c;
            sincos(i * GOLDEN_ANGLE + turn * 2.0f * PI, s, c);
            float2 at = centre + float2(c, s) * sqrt((i + 0.5f) / BLUR_TAPS) * radius;
            float w = MayCover(Stored(at), z) ? 1.0f : 0.0f;
            sum += tex2Dlod(Scene, float4(at, 0.0f, 0.0f)).rgb * w;
            weight += w;
        }
    }
    return sum / weight;
}

// Where a look's mirage comes from: the same look turned up as far as it points down.
float2 Mirror(float3 direction)
{
    return OnScreen(float3(direction.xy, abs(direction.z)));
}

// Whether a look at `uv`, `stored` metres out, lands on bare ground rather than on grass or leaves
// standing on it.
bool Bare(float2 uv, float stored, float sine)
{
    float solid = SolidMetresWith(uv, Solid.xy);
    return IsLand(stored) && solid > 0.0f && (solid - stored) * sine < GRASS_HEIGHT;
}

// How much of a mirage a look at `uv` that lands at `at` belongs to, from nought to one. Only bare
// level ground far off has one: not a hill, a rock or grass.
float Mirage(float2 uv, float3 direction, float3 at, float z, float wobble)
{
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
    float edge = Heat.z * (1.0f + RAGGED * wobble);
    float band = grazing > 0.0f ? smoothstep(edge, 0.5f * edge, grazing) : 0.0f;
    float level = smoothstep(Level.x, Level.y, normal.z);
    float distance = smoothstep(Level.z, Level.w, length(at));

    // Only what lies beyond the ground it would cover: the sky, or land farther off.
    float2 mirror = Mirror(direction);
    float shown = Stored(mirror);
    bool far = IsSky(shown) || shown > z;
    bool inside = all(mirror == saturate(mirror));
    return far && inside ? band * level * distance : 0.0f;
}

// Where a mirage shows, at half resolution.
float4 MaskPS(float2 screen : VPOS) : COLOR0
{
    float2 uv = (screen + 0.5f) * Solid.zw;
    float z = Stored(uv);
    float3 ray = ViewRay(uv);
    float3 direction = normalize(ray);
    float depression = -direction.z;
    float mirage = 0.0f;
    // The steepest look that could still meet ground tilted away at the steepest slope allowed.
    float search = Heat.z * (1.0f + RAGGED * SHIMMER_GAIN) + sqrt(1.0f - Level.x * Level.x);
    [branch] if (IsLand(z) && depression > 0.0f && depression < search)
    {
        mirage = Mirage(uv, direction, ray * z, z, Shimmer(direction).z);
    }
    return float4(mirage, 0.0f, 0.0f, 1.0f);
}

// One axis of a blur, of the mask or of the far world.
float4 BlurPS(float2 screen : VPOS) : COLOR0
{
    float4 sum = 0.0f;
    float weight = 0.0f;
    [unroll] for (int i = -BLUR_RADIUS; i <= BLUR_RADIUS; i++)
    {
        float w = BLUR_WEIGHTS[abs(i)];
        float2 at = (screen + 0.5f + Axis.xy * i) * Axis.zw;
        sum += tex2Dlod(Scene, float4(at, 0.0f, 0.0f)) * w;
        weight += w;
    }
    return sum / weight;
}

// How far off what was drawn at `uv` is, from nought where it is near enough to stay sharp to one
// for the sky. The weapon is nought.
float Farness(float2 uv)
{
    float stored = Stored(uv);
    return IsSky(stored) ? 1.0f
                         : smoothstep(NEAR_SHARE * Far.x, Far.x, stored * length(ViewRay(uv)));
}

// The far world at quarter resolution, its colour multiplied by how far it is, so near things never
// spread into the blur.
float4 DownPS(float2 screen : VPOS) : COLOR0
{
    float2 centre = (screen + 0.5f) * Axis.zw;
    float4 sum = 0.0f;
    // Four filtered taps, each the mean of a two by two block, over the four by four this covers.
    [unroll] for (int i = 0; i < 4; i++)
    {
        float2 at = centre + float2(i % 2 == 0 ? -1.0f : 1.0f, i < 2 ? -1.0f : 1.0f) * Lens.zw;
        float far = Farness(at);
        sum += float4(tex2Dlod(Scene, float4(at, 0.0f, 0.0f)).rgb * far, far);
    }
    return sum * 0.25f;
}

float4 MainPS(float2 screen : VPOS) : COLOR0
{
    float2 uv = (screen + 0.5f) * Lens.zw;
    float3 ray = ViewRay(uv);
    float stored = Stored(uv);

    // The sky just over a hot skyline shimmers with the land under it.
    float z = stored;
    [branch] if (IsSky(stored))
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
    float2 shift = shimmer.xy * float2(SIDEWAYS, 1.0f) * toUv;
    float2 bent = MayCover(Stored(uv + shift), z) ? uv + shift : uv;
    float turn = Dither(screen);
    float3 colour = Look(bent, BLUR_SHARE * toUv, turn, z);

    // Far off, the land melts into blobs that dance with the air as far as it ever bends a look.
    [branch] if (Far.z > 0.0f)
    {
        float2 dance = shimmer.xy * float2(SIDEWAYS, 1.0f) * Heat.x * MOST_GROWTH / (2.0f * Lens.xy);
        float4 blurred = tex2Dlod(Blurred, float4(uv + dance, 0.0f, 0.0f));
        float melt = Far.z * smoothstep(Far.x, Far.y, length(at)) * saturate(2.0f * blurred.a);
        colour = lerp(colour, blurred.rgb / max(blurred.a, 1e-3f), melt);
    }

    // Soft at its edges, and over whatever small stands inside or beside it.
    [branch] if (Heat.y > 0.0f)
    {
        float mirage = Heat.y * smoothstep(SPREAD.x, SPREAD.y,
                                           tex2Dlod(Mask, float4(uv, 0.0f, 0.0f)).r);
        [branch] if (mirage > 0.0f)
        {
            float2 radius = max(BLUR_SHARE * toUv, MIRAGE_BLUR * Lens.zw);
            float3 mirrored = Look(Mirror(direction) + shift, radius, turn, 0.0f);
            colour = lerp(colour, mirrored, mirage);
        }
    }
    return float4(colour, 1.0f);
}
