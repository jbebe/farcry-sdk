// Heat haze and mirage: the world seen through the hot air over sunlit ground. See src/haze.cpp for
// the passes.

#include "depth.inc.fx"
#include "hash.inc.fx"
#include "solid.inc.fx"

// The world in MainPS; the mask being blurred in BlurPS.
sampler2D Scene : register(s0);
// Where a mirage shows, blurred, at half resolution: red is how much.
sampler2D Mask : register(s2);
// Three independent fields of turbulence in [0, 1], TILE cells across, repeating on every axis.
sampler3D Turbulence : register(s4);

// x  how far one sheet of hot air SHEET_REFERENCE metres deep bends a look, in radians
// y  how much of the ground a mirage replaces
// z  the sine of the steepest look onto bare level ground that still shows a mirage
// w  the height over the ground at which the air is half as hot, in metres
float4 Heat : register(c96);

// x  seconds, for the shimmer's motion
// y  how far the view is zoomed in, against the widest it has been
// z  how wide one eddy of hot air is, in metres
// w  how tall one is
float4 Time : register(c97);

// xy  what the solid depth is linearised with
// zw  one pixel of the mirage's mask, in texture coordinates
float4 Solid : register(c98);

// x  the up component of the steepest ground's normal that still shows a mirage
// y  the same where it shows whole
// zw the distances over which a mirage comes in, in metres
float4 Level : register(c99);

// x  the distance at which the air starts to push harder and smudge, in metres
// y  the distance at which it does so wholly
// z  how many times harder the air pushes a look there than its sheets alone would
float4 Far : register(c100);

// xy  the axis of a blur pass, one pixel of its target long
// zw  one pixel of that target, in texture coordinates
float4 Axis : register(c101);

#define PI 3.14159265f

// The air between the eye and what it sees, as a few sheets of turbulence, in metres: a look
// collects the push of every sheet it crosses. Few enough that each one's waves still read.
#define SHEETS 3
static const float SHEET_DISTANCES[SHEETS] = {15.0f, 50.0f, 150.0f};

// The depth of air a sheet stands for at which it bends a look as hard as Heat.x says; deeper
// sheets bend harder, as this power of their depth.
static const float SHEET_REFERENCE = 100.0f;
static const float SHEET_GROWTH = 0.75f;

// The eye's height over the ground under it, in metres.
static const float EYE_HEIGHT = 1.8f;

// The smallest a wave may get on screen, across and up, in pixels: finer ones would average away.
static const float2 SMALLEST_WAVE = float2(4.0f, 2.0f);

// Cells across the turbulence volume, on every axis.
static const float TILE = 16.0f;

// What each sheet's turbulence is scaled by.
static const float SHIMMER_GAIN = 2.0f;

// How fast the shimmer drifts sideways, in cells a second, and how fast it churns in place.
static const float DRIFT = 3.0f;
static const float CHURN = 4.0f;

// How long the eye gathers light from near things for, in seconds, and how many moments of it each
// pixel takes: the air moving within that time smears what is seen through it.
static const float EXPOSURE = 0.04f;
#define MOMENTS 5

// How far either way along its push each moment is smudged from Far.y on, as a share of it.
static const float SMUDGE = 0.8f;

// How far below a pixel of sky to look for the land it sits over, in pixels.
static const float SKYLINE_REACH = 12.0f;

// The share of the shimmer that moves a look sideways rather than up and down.
static const float SIDEWAYS = 0.5f;

// How wide a mirage's picture is blurred, as a share of how far the air pushes it, and the least
// radius worth blurring, in pixels.
static const float BLUR_SHARE = 0.6f;
static const float LEAST_BLUR = 0.5f;

// The least blur of a mirage's own picture, in pixels.
static const float MIRAGE_BLUR = 2.0f;

// The blur's taps beside the centre, laid on a spiral by the golden angle so they cover the disc.
#define BLUR_TAPS 6
static const float GOLDEN_ANGLE = 2.39996323f;

// How far the ragged edge of a mirage reaches past Heat.z, as a share of it, and the distance of
// the sheet of air that makes it ragged.
static const float RAGGED = 0.4f;
static const float MIRAGE_SHEET = 80.0f;

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


// The turbulence of the sheet `distance` metres out where a look `around` radians round the
// horizon and `rise` up (a sine) crosses it, at `time`: three fields in [-0.5, 0.5). Its eddies keep
// their size in the world, so a far sheet's are small on screen, down to SMALLEST_WAVE; the whole
// is scaled by the zoom, so they keep their size on screen through a scope.
float3 Sheet(float around, float rise, float distance, float time, float index)
{
    float pixel = 2.0f * Lens.y * Lens.w * Time.y;
    float2 wave = max(Time.zw / distance, SMALLEST_WAVE * pixel) / Time.y;
    float cellsAround = max(round(2.0f * PI / (wave.x * TILE)), 1.0f) * TILE;
    float3 at = float3(around * cellsAround / (2.0f * PI) - time * DRIFT, rise / wave.y,
                       time * CHURN + index * 5.3f);
    return tex3Dlod(Turbulence, float4(at / TILE, 0.0f)).rgb - 0.5f;
}

// How hot the air is `height` metres over the ground that heats it, against the ground's own:
// half at Heat.w, and falling ever more slowly above it, so the haze thins up a hillside rather
// than stopping.
float Hotness(float height)
{
    return 1.0f / (1.0f + max(height, 0.0f) / Heat.w);
}

// How far the air pushes a look `distance` metres long at `time`, in radians, and how ragged it is
// in z: every sheet it crosses before it gets there, each the more the farther it stands from what
// is seen, and the hotter the nearer the look runs to `ground`, the height below the eye of the
// low land the air lies on.
float3 Push(float around, float rise, float distance, float time, float ground)
{
    float3 sum = 0.0f;
    [unroll] for (int k = 0; k < SHEETS; k++)
    {
        float sheet = SHEET_DISTANCES[k];
        float lever = sqrt(saturate(1.0f - sheet / distance));
        float weight = pow(sheet / SHEET_REFERENCE, SHEET_GROWTH) * lever *
                       Hotness(sheet * rise - ground);
        sum += Sheet(around, rise, sheet, time, k) * weight;
    }
    return sum * SHIMMER_GAIN * float3(Heat.x, Heat.x, 1.0f);
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
        float wobble = Sheet(atan2(direction.y, direction.x), direction.z, MIRAGE_SHEET, Time.x,
                             0.0f).z;
        mirage = Mirage(uv, direction, ray * z, z, SHIMMER_GAIN * wobble);
    }
    return float4(mirage, 0.0f, 0.0f, 1.0f);
}

// One axis of the mask's blur.
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
    float distance = length(at);
    float3 direction = at / distance;
    float around = atan2(direction.y, direction.x);
    float2 toUv = float2(SIDEWAYS, 1.0f) / (2.0f * Lens.xy);
    float turn = Dither(screen);

    // The hot air lies on the lower of the ground under the eye and the ground looked at, so a
    // hilltop seen over a plain is seen through cool air and the hill's foot through hot.
    float ground = min(-EYE_HEIGHT, at.z);

    // Each moment the eye gathers sees the world pushed its own way, so what moves within one is
    // smeared along the way it moves. A pixel's moments start at its own share of the exposure, so
    // neighbours fill in each other's gaps. Far off the air pushes harder, and each moment is
    // smudged along its own push, so the smudge moves with the air rather than settling.
    float far = smoothstep(Far.x, Far.y, distance) * Hotness(at.z - ground);
    float wobble = lerp(1.0f, Far.z, far);
    float3 colour = 0.0f;
    float weight = 0.0f;
    float2 shift = 0.0f;
    float2 spread = 0.0f;
    [unroll] for (int i = 0; i < MOMENTS; i++)
    {
        float time = Time.x - EXPOSURE * (i + turn) / MOMENTS;
        float2 push = Push(around, direction.z, distance, time, ground).xy * toUv * wobble;
        shift += push / MOMENTS;
        spread += abs(push) / MOMENTS;
        float along = (i + turn) / MOMENTS * 2.0f - 1.0f;
        float2 tap = uv + push * (1.0f + SMUDGE * far * along);
        float w = MayCover(Stored(tap), z) ? 1.0f : 0.0f;
        colour += tex2Dlod(Scene, float4(tap, 0.0f, 0.0f)).rgb * w;
        weight += w;
    }
    colour = weight > 0.0f ? colour / weight : tex2Dlod(Scene, float4(uv, 0.0f, 0.0f)).rgb;

    // Soft at its edges, and over whatever small stands inside or beside it.
    [branch] if (Heat.y > 0.0f)
    {
        float mirage = Heat.y * smoothstep(SPREAD.x, SPREAD.y,
                                           tex2Dlod(Mask, float4(uv, 0.0f, 0.0f)).r);
        [branch] if (mirage > 0.0f)
        {
            float2 radius = max(BLUR_SHARE * spread, MIRAGE_BLUR * Lens.zw);
            float3 mirrored = Look(Mirror(direction) + shift, radius, turn, 0.0f);
            colour = lerp(colour, mirrored, mirage);
        }
    }
    return float4(colour, 1.0f);
}
