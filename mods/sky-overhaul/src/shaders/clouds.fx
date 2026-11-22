// The clouds, drawn over the world's own sky pass in place of the engine's.
//
// The output convention is the engine's own cloud layer's, so that the bloom and tone mapping
// after it see what they saw before: colour premultiplied and scaled by the exposure, alpha the
// transmittance, blended with (one, source alpha).

// How many samples the view ray takes through the layer, and how many the light ray takes toward
// the light from each of those. Fixed rather than a constant so the loops unroll and the shader
// needs no integer registers.
#define VIEW_STEPS 48
#define LIGHT_STEPS 5

// How far apart two samples may be before the march starts stepping over whole clouds. A ray near
// the horizon runs almost along the layer and would otherwise spread its samples over kilometres.
#define MAX_STRIDE 90.0f

// How much light the high sheet bends forward. Gentle: enough that it lifts toward the sun, not
// so much that it only exists there.
#define CIRRUS_FORWARD 0.35f

// How much of the light the sheet returns. Ice scatters more of what reaches it than water does,
// and there is no depth for any of it to be lost in.
#define CIRRUS_ALBEDO 1.1f

// How tightly the corona around the moon hugs it: a tenth of its brightness at the disc's edge is
// left five degrees out. The cosines of the angle off the moon's centre where it starts, at the
// disc's edge 1.75 degrees out, and where it is full.
#define MOON_GLOW_FORWARD 0.97f
#define MOON_DISC_EDGE 0.999534f
#define MOON_CORONA_FULL 0.999391f

// How many aircraft trails a day can hold.
#define TRAIL_COUNT 3

// How far a trail wanders off the line the aircraft actually flew, as a multiple of its own width,
// and how much of that wander is the shorter kink rather than the long slow drift. Enough that no
// stretch of it reads as drawn, short of the squiggle that would stop reading as a trail at all.
#define TRAIL_WANDER 5.0f
#define TRAIL_KINK 0.35f

// How deep the hazy air under the sheet effectively is, in metres, which is the distance a ray
// straight up spends in it. Everything below is that same depth divided by how slanted the ray is.
#define CIRRUS_AIR 900.0f

// The planet a cloud sits above, in metres, which is what hides the light from it once the light
// has set by more than the horizon dips at that height - about a degree a kilometre up.
#define PLANET_RADIUS 6371000.0f

// Over how much of the light's elevation, as a sine, a cloud goes from lit to the earth's shadow:
// about the width of the sun or the moon, so the light leaves a cloud the way it leaves a hilltop.
#define LIGHT_PENUMBRA 0.01f

#define PI 3.14159265f
#define LUMA float3(0.299f, 0.587f, 0.114f)

// How many samples a patch of ground takes up through the layer toward the sun, for its shadow.
#define SHADOW_STEPS 6

// Where the cloud shadows start to fade and where they are gone, in metres: the fog has the land
// by then.
#define SHADOW_FADE_START 300.0f
#define SHADOW_FADE_END 600.0f

#include "hash.inc.fx"
#include "horizon.inc.fx"
#include "shadows.inc.fx"

// The low frequencies a cloud's body is carved from, the high ones its edges are eroded by, and
// where over the world clouds stand at all.
sampler3D ShapeNoise : register(s0);
sampler3D DetailNoise : register(s1);
sampler2D WeatherMap : register(s2);

// The weather's cells over the map, as cell_map.cpp draws them: r how much of the layer they fill,
// g how tall they stand, b how dark and wet they are, a their anvils. The map is 40960 m square
// round the middle of the campaign world.
sampler2D CellMap : register(s5);
#define CELL_MAP_SCALE (1.0f / 40960.0f)
#define CELL_MAP_OFFSET 0.4375f
// How solid a cell at its wettest is, how dark its base, how much sunlight its depth leaves its
// underside, how far over the sky its anvil's ice reaches and how solid that is, how much of the
// sky it fills where the weather map is empty, and how much its raining heart fills at least.
#define CELL_DENSITY 0.04f
#define CELL_SHADE 0.85f
#define CELL_SUN 0.4f
#define CELL_ANVIL_REACH 0.7f
#define CELL_ANVIL 0.35f
// How many times finer than the sheet an anvil's ice is torn.
#define CELL_ANVIL_GRAIN 5.0f
#define CELL_FILL 0.3f
#define CELL_CORE 0.85f

// xyz: where the camera is, in world space with Z up. w: the frame's exposure.
float4 Eye : register(c71);
// x: the layer's floor. y: how deep it is. z: how much of the sky it fills. w: how solid it is.
float4 Layer : register(c72);
// xy: how far the shape has drifted. zw: how far the weather has.
float4 Wind : register(c73);
// x, y, z: how many metres one repeat of the shape, the detail and the weather covers.
// w: how hard the detail bites into the edges.
float4 Grain : register(c74);
// xyz: the direction of the sun or the moon. w: how much light bends forward off a droplet.
float4 Light : register(c75);
// rgb: that light's colour. w: how far apart the samples toward it are.
float4 LightColour : register(c76);
// rgb: what the sky and ground light the cloud with from every other direction. w: how much of that
// a storm takes away at the layer's base, less toward its top.
float4 AmbientColour : register(c77);
// rgb: what reaches the eye through a thin edge, which is what makes a silver lining. w: how wide a
// half-resolution pixel is, in radians.
float4 BackColour : register(c78);
// How many texels one repeat of the shape noise spans.
#define SHAPE_TEXELS 128.0f
// x: how far out clouds are drawn. y: over what distance they fade to nothing before that.
// z: how far the march itself runs, the rest being left to the haze. w: over how far a cloud
// turns into that haze.
float4 Range : register(c79);

// x: the altitude of the high sheet. y: how many metres one repeat of it covers. z: how much of
// the sky it reaches across. w: how solid it is where it does.
float4 Cirrus : register(c85);
// x: how strongly aircraft trails show. y: how wide a fresh one is. z: over what length one comes
// and goes.
float4 Trail : register(c86);
// rgb: how brightly the air glows right beside the moon, and nothing while the sun lights the sky.
// w: how far a storm drains everything drawn here toward grey.
float4 MoonGlow : register(c87);
// Where today's aircraft went. xy: a trail's unit normal. z: how far it sits from the world's origin
// along that, in the units the sheet is read in. w: the seed that tears it up, or nought for no trail.
float4 Trails[TRAIL_COUNT] : register(c88);

// Lightning in the cloud: xyz where the strike is, w how bright, for the two brightest now.
float4 Flash[2] : register(c96);
// How far through a cloud a strike's light reaches before a third of it is left, and its colour.
#define FLASH_REACH 900.0f
#define FLASH_COLOUR float3(0.82f, 0.88f, 1.0f)

float3 Flashes(float3 at) {
    float glow = 0.0f;
    [unroll] for (int i = 0; i < 2; i++) {
        glow += Flash[i].w * exp(-length(at - Flash[i].xyz) / FLASH_REACH);
    }
    return FLASH_COLOUR * glow;
}

// The engine's own sky fog, register for register, so our clouds sit in the same haze the dome
// does. See docs/docs/engine-internals/sky-and-clouds.md. FogColour.w is ours: how far the weather's
// fog swallows the land before the horizon, as Veiled takes it.
float4 FogColour : register(c80);
float4 FogColourRange : register(c81);
float4 FogValues : register(c82);
float4 FogHeightValues : register(c83);
// xy: the engine's fog vector. zw: our sky's filled land, as Filled takes it.
float4 FogColourVector : register(c84);

float Remap(float value, float fromLow, float fromHigh, float toLow, float toHigh) {
    return toLow + (value - fromLow) / (fromHigh - fromLow) * (toHigh - toLow);
}

// A cloud's base really is flat, and at the same altitude across the whole sky: it is the height
// where rising air becomes cold enough for its water to condense. Its top is not, so the falloff
// there is long and rounded, and the shape noise breaks it up further.
float HeightProfile(float height, float tallness) {
    float top = lerp(0.25f, 1.0f, tallness);
    float base = saturate(Remap(height, 0.0f, 0.08f, 0.0f, 1.0f));
    float crown = saturate(Remap(height, top * 0.25f, top, 1.0f, 0.0f));
    return base * crown * crown;
}

float4 CellAt(float2 world) {
    return tex2Dlod(CellMap, float4(world * CELL_MAP_SCALE + CELL_MAP_OFFSET, 0.0f, 0.0f));
}

// How much cloud stands at a point. `cheap` skips the erosion, which is most of the cost and none
// of the shape, and is what the samples toward the light use.
float Density(float3 world, uniform bool cheap) {
    float height = saturate((world.z - Layer.x) / Layer.y);

    float2 weatherUv = (world.xy + Wind.zw) * Grain.z;
    float3 weather = tex2Dlod(WeatherMap, float4(weatherUv, 0.0f, 0.0f)).rgb;
    float4 cell = CellAt(world.xy);
    // Half is neutral, so the slider opens the sky out from the map or closes it down to nothing.
    // A cell fills the sky toward a crowd of towers, whose edges the shape below still carves, and
    // closes it over its raining heart.
    float cloudiness = saturate(max(lerp(weather.b + Layer.z * 2.0f - 1.0f,
                                         CELL_FILL + 0.5f * weather.b, cell.r),
                                    CELL_CORE * cell.b));
    float solid = max(Layer.w, CELL_DENSITY * cell.b);

    // The layer is a few hundred metres deep and the shape repeats over thousands, so read across
    // it far faster than along it. Sampled at one scale in all three axes, a layer this thin cuts
    // an almost constant slice out of the volume and every cloud in it comes out the same height.
    float acrossLayer = 1.0f / max(Layer.y * 3.0f, 1.0f);
    float3 shapeUv = float3((world.xy + Wind.xy) * Grain.x, world.z * acrossLayer);
    float4 shape = tex3Dlod(ShapeNoise, float4(shapeUv, 0.0f));

    // Three frequencies of billow, folded into one field, then used to carve the fourth.
    float billow = shape.g * 0.625f + shape.b * 0.25f + shape.a * 0.125f;
    float body = saturate(Remap(shape.r, billow - 1.0f, 1.0f, 0.0f, 1.0f));
    body *= HeightProfile(height, max(weather.r, cell.g));

    float density = saturate(Remap(body, 1.0f - cloudiness, 1.0f, 0.0f, 1.0f));

    if (cheap || density <= 0.0f) {
        return density * solid;
    }

    // Wispy at the base and billowy above it, which is how a real cloud's edge tears.
    float3 detail = tex3Dlod(DetailNoise, float4(world * Grain.y, 0.0f)).rgb;
    float fine = detail.r * 0.625f + detail.g * 0.25f + detail.b * 0.125f;
    float erosion = lerp(1.0f - fine, fine, saturate(height * 4.0f));
    density = saturate(Remap(density, erosion * Grain.w, 1.0f, 0.0f, 1.0f));
    return density * solid;
}

// Whether the light is still above the horizon as seen from this height, from one in full view to
// nought in the earth's shadow. Measuring it only through the cloud in front of it would light a
// cloud from underneath all night, because a light below the horizon still has a short way out
// through the cloud's own base.
float LightAbove(float height) {
    float dip = sqrt(max(2.0f * height / PLANET_RADIUS, 0.0f));
    return smoothstep(-dip - LIGHT_PENUMBRA, -dip + LIGHT_PENUMBRA, Light.z);
}

// Most light carries on forward past a droplet, which is what makes a cloud glare when it stands
// in front of the sun. The floor under the lobe stands for the light that has already bounced so
// many times inside the cloud that it has forgotten which way it came in: without it a cloud is
// black everywhere except toward the sun.
float Phase(float cosAngle, float forward) {
    float squared = forward * forward;
    float lobe = (1.0f - squared) /
                 pow(max(1.0f + squared - 2.0f * forward * cosAngle, 0.0001f), 1.5f);
    return lerp(1.0f, lobe, 0.35f);
}

// Light that reached this point after any number of bounces, as three orders of scattering: each
// one dimmer, spread wider, and reaching deeper into the cloud than the last. The deeper reach is
// the whole trick, and it costs nothing, because a thinner cloud's transmittance is the one
// already measured raised to a lesser power.
float3 Scatter(float lit, float3 lobes) {
    float total = 0.0f;
    float brightness = 1.0f;
    float depth = 1.0f;
    [unroll] for (int order = 0; order < 3; order++) {
        total += lobes[order] * pow(lit, depth) * brightness;
        brightness *= 0.55f;
        depth *= 0.5f;
    }
    return LightColour.rgb * total;
}

// What one aircraft left behind: a line at the sheet's altitude, narrow, and neither straight,
// uniform nor endless. `path` is the line's unit normal and how far it sits from the origin, in
// the same units the sheet is read in.
float Contrail(float2 at, float3 path, float seed) {
    if (seed <= 0.0f) {
        return 0.0f;
    }
    float across = dot(at, path.xy) - path.z;
    float along = dot(at, float2(-path.y, path.x));

    // The aircraft flew straight; what it left did not stay that way. Air at that altitude does not
    // move as one piece, so the trail is dragged sideways by however fast the layer it happens to
    // be lying in is going - a long slow drift with a shorter kink riding on it. Both are read
    // along the length only, so the whole width swings together and the edges stay clean: a trail
    // wanders, it does not fray.
    float drift = tex3Dlod(ShapeNoise,
                           float4(along * Trail.z * 0.35f, seed + 0.11f, 0.61f, 0.0f)).r;
    float kink = tex3Dlod(ShapeNoise,
                          float4(along * Trail.z * 1.70f, seed + 0.11f, 0.83f, 0.0f)).r;
    across -= Trail.y * TRAIL_WANDER * ((drift - 0.5f) + (kink - 0.5f) * TRAIL_KINK);
    across = abs(across);

    // Whether there is a trail here at all, read along its length only, so a gap runs clean across
    // the width the way a real one does. One aircraft passed once and what it left has been
    // tearing apart ever since, so it arrives in lengths with sky between them rather than running
    // unbroken from one horizon to the other. The seed is what stops both aircraft from having
    // torn up in exactly the same places.
    float presence = tex3Dlod(ShapeNoise, float4(along * Trail.z, seed, 0.29f, 0.0f)).r;
    presence = saturate(Remap(presence, 0.45f, 0.68f, 0.0f, 1.0f));

    // Wider where it is older, which is also where it is fainter: a trail does not end, it spreads
    // until it is no longer a line. And wider in some places than others along the way, because the
    // air it is spreading into is no more even than the air that moved it.
    float puff = tex3Dlod(ShapeNoise, float4(along * Trail.z * 2.30f, seed + 0.41f, 0.17f, 0.0f)).r;
    float spread = Trail.y * (1.0f + (1.0f - presence) * 2.5f) * (0.55f + puff);
    float core = saturate(1.0f - across / spread);
    return core * core * presence;
}

// The high sheet. Ice rather than water, thin enough that the sun passes almost straight through
// it, so there is no depth to march and it costs one intersection and one sample.
//
// The shape is layered noise taken as it comes, the way a paint program's cloud filter makes it:
// nothing stretched, folded or warped. How much of the field survives is one control and how
// solid what survives is is another, because they are two different things about a sky.
float3 HighCloud(float3 ray, float cosAngle, float3 horizon, out float cover) {
    cover = 0.0f;
    float rise = Cirrus.x - Eye.z;
    if (ray.z <= 0.02f || rise <= 0.0f) {
        return 0.0f;
    }

    // A cell's anvil spreads the sheet over it, as far and as solid as the anvil has grown.
    float travel = rise / ray.z;
    float2 below = Eye.xy + ray.xy * travel;
    float anvil = CellAt(below).a;
    if (Cirrus.z <= 0.0f && anvil <= 0.0f) {
        return 0.0f;
    }
    float2 overhead = below * Cirrus.y;
    float2 at = overhead + Wind.xy * 2.5f * Cirrus.y;

    // Read as blurred as a pixel's footprint along the sheet is wide, so far out it evens into a
    // haze rather than its repeats beating into stripes.
    float footprint = BackColour.w * travel / ray.z * Cirrus.y * SHAPE_TEXELS;
    float lod = log2(max(footprint, 1.0f));
    float field = tex3Dlod(ShapeNoise, float4(at, 0.37f, lod)).r;
    cover = saturate(Remap(field, 1.0f - Cirrus.z, 1.0f, 0.0f, 1.0f)) * Cirrus.w;
    if (anvil > 0.0f) {
        float torn =
            tex3Dlod(ShapeNoise, float4(at * CELL_ANVIL_GRAIN, 0.53f, lod + log2(CELL_ANVIL_GRAIN))).r;
        float ice = saturate(Remap(torn, 1.0f - anvil * CELL_ANVIL_REACH, 1.0f, 0.0f, 1.0f));
        cover = max(cover, ice * anvil * CELL_ANVIL);
    }

    // Added rather than blended in, because a trail is ice laid on top of whatever sky was already
    // there and does not care how much cirrus it crosses. They stay over the world, not the wind.
    float trails = 0.0f;
    [unroll] for (int i = 0; i < TRAIL_COUNT; i++) {
        trails += Contrail(overhead, Trails[i].xyz, Trails[i].w);
    }
    cover = saturate(cover + trails * Trail.x);

    // Haze is made by the air near the ground, and a sheet this high is above almost all of it.
    // What dims it is not how far the ray went but how slanted it was while crossing that air:
    // straight up crosses the layer once, and a ray near the horizon crosses many times as much of
    // the same layer. Measuring the distance travelled instead leaves cirrus visible only in a
    // cone overhead, because ten kilometres of it are gone by thirty degrees.
    float airMass = CIRRUS_AIR / max(ray.z, 0.02f);
    float lost = 1.0f - exp(-airMass / max(Range.w, 1.0f));

    // Ice does scatter forward harder than water does, but a sheet with a sharp lobe on it stops
    // being cloud and becomes a ring around the sun: at eight tenths the peak is forty-five times
    // the rest of the sky, which the sun disc behind it is already busy filling.
    float3 light =
        LightColour.rgb * Phase(cosAngle, CIRRUS_FORWARD) * CIRRUS_ALBEDO * LightAbove(Cirrus.x) +
        AmbientColour.rgb;
    return lerp(light, horizon, lost);
}

// How much of the light reaches a point, by marching toward it and counting what is in the way.
float LightReach(float3 world) {
    float depth = 0.0f;
    [loop] for (int i = 0; i < LIGHT_STEPS; i++) {
        float3 at = world + Light.xyz * ((float)i + 0.5f) * LightColour.w;
        // Past the floor or the ceiling the march only moves further out, where there is no cloud.
        float height = (at.z - Layer.x) / Layer.y;
        if (height <= 0.0f || height >= 1.0f) {
            break;
        }
        depth += Density(at, true);
    }
    return exp(-depth * LightColour.w);
}

// The glow of the air around the moon along a ray: none on the disc, so its face stays its own,
// then falling away the way a forward-scattering lobe does.
float3 MoonHalo(float cosAngle) {
    float g = MOON_GLOW_FORWARD;
    float falloff = (1.0f - g) * (1.0f - g) / max(1.0f + g * g - 2.0f * g * cosAngle, 1e-6f);
    return MoonGlow.rgb * pow(falloff, 1.5f) *
           (1.0f - smoothstep(MOON_CORONA_FULL, MOON_DISC_EDGE, cosAngle));
}

// Where a ray is inside the layer: how far out it enters, how far apart the march's samples are, and
// how much of it the distance fade leaves. Below the horizon, or above a layer the camera has
// climbed over, there is nothing to march.
void Span(float3 ray, out float enter, out float stride, out float reach) {
    float up = max(ray.z, 0.0001f);
    float toFloor = (Layer.x - Eye.z) / up;
    float toCeiling = (Layer.x + Layer.y - Eye.z) / up;
    enter = max(min(toFloor, toCeiling), 0.0f);
    float leave = min(max(toFloor, toCeiling), Range.x);

    // The march covers only the near part of the layer. Spreading the same samples over the whole
    // of it would step clean past clouds near the horizon, where the ray runs almost along the
    // layer, and the shape would repeat visibly out there in any case.
    leave = min(leave, enter + Range.z);

    reach = saturate((Range.x - enter) / Range.y) * step(0.001f, ray.z) * step(enter, leave);
    stride = min(max(leave - enter, 0.0f) / VIEW_STEPS, MAX_STRIDE);
}


float4 March(float3 rayIn, float2 screen) {
    float3 ray = normalize(rayIn);
    float enter;
    float stride;
    float reach;
    Span(ray, enter, stride, reach);
    float dither = Dither(screen);

    float cosAngle = dot(ray, Light.xyz);

    // Each order of scattering's lobe toward the eye, the forward bend halving with every order.
    float3 lobes = float3(Phase(cosAngle, Light.w), Phase(cosAngle, Light.w * 0.5f),
                          Phase(cosAngle, Light.w * 0.25f));

    // Taken once at the middle of the layer rather than at every sample: the whole layer loses the
    // light within the same degree, and a degree of its travel is minutes of the day.
    float above = LightAbove(Layer.x + 0.5f * Layer.y);

    // The colour the sky goes toward at the horizon, which is where everything below ends up: the
    // engine's own, by heading against the sun, so it matches the dome rather than approximating
    // it. Worked out before either layer, because both fade into it.
    float fogHeading =
        acos(clamp(dot(normalize(ray.xy + 0.0001f), FogColourVector.xy), -1.0f, 1.0f)) / PI;
    float3 horizon = Veiled(FogColour.rgb + FogColourRange.rgb * fogHeading, FogColour.w);

    float cirrusCover = 0.0f;
    float3 cirrus = HighCloud(ray, cosAngle, horizon, cirrusCover);

    float transmittance = 1.0f;
    float3 scattered = 0.0f;
    [loop] for (int i = 0; i < VIEW_STEPS; i++) {
        // Nothing further along can show: a ray with no reach is multiplied away below, and cloud
        // this solid already hides whatever lies behind it.
        if (reach <= 0.0f || transmittance < 0.01f) {
            break;
        }
        float3 at = Eye.xyz + ray * (enter + ((float)i + dither) * stride);
        float density = Density(at, false);
        if (density > 0.0005f) {
            float lit = LightReach(at);

            // Dark where the cloud is thin and the light has not yet scattered into it. Only worth
            // anything looking toward the light, where it is what keeps a bright edge from reading
            // as paper; away from it it would only make an already dim cloud dimmer.
            float powder = 1.0f - exp(-density * 8.0f);
            float thin = lerp(1.0f, powder, saturate(cosAngle));

            // A cell stands far deeper than the layer, which the light reaching its base makes up for.
            float height = saturate((at.z - Layer.x) / Layer.y);
            float water = CellAt(at.xy).b;
            float shade = max(AmbientColour.w, CELL_SHADE * water);
            float3 light = (Scatter(lit, lobes) * thin +
                            BackColour.rgb * lit * saturate(-cosAngle)) * above *
                               lerp(1.0f, CELL_SUN, water * (1.0f - height)) +
                           AmbientColour.rgb * lerp(1.0f - shade, 1.0f, height) + Flashes(at);

            float transmit = exp(-density * stride);
            scattered += light * transmittance * (1.0f - transmit);
            transmittance *= transmit;
        }
    }

    // The sky's own fog, whose amount is a function of height up the dome the engine draws rather
    // than of distance.
    float fogHeight = saturate(ray.z * 180.0f * FogHeightValues.x + FogHeightValues.y);
    float fog = saturate((fogHeight * FogHeightValues.z + FogHeightValues.w) * FogValues.z);

    // And distance on top of it, which the sky's own fog has no term for because the dome it was
    // written for is a fixed shape. A layer runs to the horizon, so without this the far half of
    // it stays as crisp as the near half and its repeats become a pattern in the sky.
    //
    // Air scatters a constant fraction of what passes through each metre of it, so what survives
    // falls away exponentially rather than in a straight line: near clouds keep almost all of
    // their own colour, far ones are almost entirely the horizon's, and there is no distance at
    // which the change announces itself.
    float distant = 1.0f - exp(-enter / max(Range.w, 1.0f));
    fog = saturate(fog + distant * (1.0f - fog));

    float cover = (1.0f - transmittance) * reach;
    float3 colour = lerp(scattered * reach, horizon * cover, fog);

    // The sheet is above the layer, so from below it is behind it: what it sends down arrives
    // dimmed by however much of the layer stands in the way.
    float through = 1.0f - cover;
    colour += cirrus * cirrusCover * through;
    cover += cirrusCover * through;

    // The moon's glow is behind both layers, and dithered wherever it is lit: a faint gradient on a
    // black sky is where eight bits a channel band worst.
    float3 halo = MoonHalo(cosAngle);
    colour += (halo + (dither - 0.5f) / 255.0f * saturate(halo.b * 255.0f)) * (1.0f - cover);

    colour = lerp(colour, dot(colour, LUMA), MoonGlow.w);
    return float4(colour * Eye.w, 1.0f - cover);
}

// The march at half resolution, for CompositePS to lay over the sky. Only where the four by four
// pixels around a half-resolution pixel hold sky: its own two by two, and the ring that CompositePS's
// filtered read reaches into from the pixels beside it.
float4 HalfPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    float2 corner = FullFromHalf(screen);
    bool sky = false;
    [unroll] for (int y = -1; y <= 2; y++) {
        [unroll] for (int x = -1; x <= 2; x++) {
            sky = sky || IsSky(Stored(corner + float2(x, y) * Lens.zw));
        }
    }
    [branch] if (!sky) {
        return float4(0.0f, 0.0f, 0.0f, 1.0f);
    }
    return March(rayIn, screen);
}

// What HalfPS drew, over the sky at full resolution.
sampler2D HalfClouds : register(s4);

float4 CompositePS(float2 screen : VPOS) : COLOR0 {
    // Each half-resolution ray was taken at its texel's corner, not its centre, so the read lands
    // half a pixel on for each full pixel to meet its own ray.
    return tex2Dlod(HalfClouds, float4((screen + 1.0f) * Lens.zw, 0.0f, 0.0f));
}

// How much of a ray the clouds cover, as March draws them, with the march offset by `dither`, and
// the filled land under the horizon with them. `cheap` leaves the edges uneroded.
float Cover(float3 rayIn, float dither, uniform bool cheap) {
    float3 ray = normalize(rayIn);
    float enter;
    float stride;
    float reach;
    Span(ray, enter, stride, reach);

    float transmittance = 1.0f;
    [loop] for (int i = 0; i < VIEW_STEPS; i++) {
        if (reach <= 0.0f || transmittance < 0.01f) {
            break;
        }
        float3 at = Eye.xyz + ray * (enter + ((float)i + dither) * stride);
        float density = Density(at, cheap);
        if (density > 0.0005f) {
            transmittance *= exp(-density * stride);
        }
    }
    float cover = (1.0f - transmittance) * reach;

    float cirrusCover = 0.0f;
    HighCloud(ray, dot(ray, Light.xyz), 0.0f, cirrusCover);
    cover += cirrusCover * (1.0f - cover);
    return cover + Filled(ray, FogColourVector.zw) * (1.0f - cover);
}

// Discards a pixel with the likelihood that the clouds or the land cover its ray.
float4 CoverPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    float dither = Dither(screen);
    clip(dither - Cover(rayIn, dither, false));
    return 0.0f;
}

// The light that reaches the eye along a ray, which a target of one texel takes toward the sun for
// the rainbow.
float4 SunlitPS(float3 rayIn : TEXCOORD0) : COLOR0 {
    return float4(LightColour.rgb * (1.0f - Cover(rayIn, 0.5f, true)), 1.0f);
}

// What the clouds and the land cover of a pixel's ray, for the god-ray mask to be multiplied down by,
// with the edges uneroded: the rays blur the mask.
float4 MaskPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    return Cover(rayIn, Dither(screen), true);
}

// What the clouds leave of the sunlight on the ground under a half-resolution pixel, in alpha,
// found by marching from the ground up through the layer toward the sun. It depends on where the
// point is and nothing else about the pixel, so it stays as smooth as the cloud over grass whose
// every blade faces its own way.
float4 ShadowPS(float2 screen : VPOS) : COLOR0 {
    float2 uv = FullFromHalf(screen);
    float z = Metres(uv);
    if (z <= 0.0f || z >= SHADOW_FADE_END) {
        return 1.0f;
    }
    float3 ground = Eye.xyz + ViewRay(uv) * z;

    float up = max(Light.z, 0.05f);
    float enter = max((Layer.x - ground.z) / up, 0.0f);
    float leave = max((Layer.x + Layer.y - ground.z) / up, enter);
    float stride = (leave - enter) / SHADOW_STEPS;
    float depth = 0.0f;
    [unroll] for (int i = 0; i < SHADOW_STEPS; i++) {
        depth += Density(ground + Light.xyz * (enter + ((float)i + 0.5f) * stride), true);
    }
    float blocked = 1.0f - exp(-depth * stride);

    float fade = 1.0f - smoothstep(SHADOW_FADE_START, SHADOW_FADE_END, z);
    return 1.0f - blocked * fade * Shade.y;
}
