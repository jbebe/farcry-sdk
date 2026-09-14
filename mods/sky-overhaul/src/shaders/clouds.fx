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

// How tightly the glow around the moon hugs it: a seventh of it is left ten degrees out.
#define MOON_GLOW_FORWARD 0.9f

// Where the two aircraft went: a unit normal and how far the line sits from the origin, in the
// units the sheet is read in. Fixed, because they are scenery rather than traffic.
//
// Their headings are twenty degrees and forty-five, so they close at twenty-five. At that angle
// two lines that both pass overhead must cross within a few kilometres, so only the first does:
// the second runs some eight kilometres to one side, and the two meet twenty kilometres out, which
// at this altitude is about seventeen degrees above the horizon. A pair of trails converging low
// and far is what the sky actually does with them.
#define TRAIL_A float3(-0.342f, 0.940f, 0.180f)
#define TRAIL_B float3(-0.707f, 0.707f, -0.356f)

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

#include "occlusion.inc.fx"

// The low frequencies a cloud's body is carved from, the high ones its edges are eroded by, and
// where over the world clouds stand at all.
sampler3D ShapeNoise : register(s0);
sampler3D DetailNoise : register(s1);
sampler2D WeatherMap : register(s2);

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
// rgb: what reaches the eye through a thin edge, which is what makes a silver lining.
float4 BackColour : register(c78);
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

// The engine's own sky fog, register for register, so our clouds sit in the same haze the dome
// does. See docs/docs/engine-internals/sky-and-clouds.md.
float4 FogColour : register(c80);
float4 FogColourRange : register(c81);
float4 FogValues : register(c82);
float4 FogHeightValues : register(c83);
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

// How much cloud stands at a point. `cheap` skips the erosion, which is most of the cost and none
// of the shape, and is what the samples toward the light use.
float Density(float3 world, uniform bool cheap) {
    float height = saturate((world.z - Layer.x) / Layer.y);

    float2 weatherUv = (world.xy + Wind.zw) * Grain.z;
    float3 weather = tex2Dlod(WeatherMap, float4(weatherUv, 0.0f, 0.0f)).rgb;
    // Half is neutral, so the slider opens the sky out from the map or closes it down to nothing.
    float cloudiness = saturate(weather.b + Layer.z * 2.0f - 1.0f);

    // The layer is a few hundred metres deep and the shape repeats over thousands, so read across
    // it far faster than along it. Sampled at one scale in all three axes, a layer this thin cuts
    // an almost constant slice out of the volume and every cloud in it comes out the same height.
    float acrossLayer = 1.0f / max(Layer.y * 3.0f, 1.0f);
    float3 shapeUv = float3((world.xy + Wind.xy) * Grain.x, world.z * acrossLayer);
    float4 shape = tex3Dlod(ShapeNoise, float4(shapeUv, 0.0f));

    // Three frequencies of billow, folded into one field, then used to carve the fourth.
    float billow = shape.g * 0.625f + shape.b * 0.25f + shape.a * 0.125f;
    float body = saturate(Remap(shape.r, billow - 1.0f, 1.0f, 0.0f, 1.0f));
    body *= HeightProfile(height, weather.r);

    float density = saturate(Remap(body, 1.0f - cloudiness, 1.0f, 0.0f, 1.0f));

    if (cheap || density <= 0.0f) {
        return density * Layer.w;
    }

    // Wispy at the base and billowy above it, which is how a real cloud's edge tears.
    float3 detail = tex3Dlod(DetailNoise, float4(world * Grain.y, 0.0f)).rgb;
    float fine = detail.r * 0.625f + detail.g * 0.25f + detail.b * 0.125f;
    float erosion = lerp(1.0f - fine, fine, saturate(height * 4.0f));
    density = saturate(Remap(density, erosion * Grain.w, 1.0f, 0.0f, 1.0f));
    return density * Layer.w;
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
    if (ray.z <= 0.02f || rise <= 0.0f || Cirrus.z <= 0.0f) {
        return 0.0f;
    }

    float travel = rise / ray.z;
    float2 at = (Eye.xy + ray.xy * travel + Wind.xy * 2.5f) * Cirrus.y;

    float field = tex3Dlod(ShapeNoise, float4(at, 0.37f, 0.0f)).r;
    cover = saturate(Remap(field, 1.0f - Cirrus.z, 1.0f, 0.0f, 1.0f)) * Cirrus.w;

    // Two aircraft, crossing. Added rather than blended in, because a trail is ice laid on top of
    // whatever sky was already there and does not care how much cirrus it crosses.
    float trails = Contrail(at, TRAIL_A, 0.23f) + Contrail(at, TRAIL_B, 0.68f);
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

// The glow of the air around the moon along a ray: all of it against the disc, falling away the
// way a forward-scattering lobe does.
float3 MoonHalo(float cosAngle) {
    float g = MOON_GLOW_FORWARD;
    float falloff = (1.0f - g) * (1.0f - g) / max(1.0f + g * g - 2.0f * g * cosAngle, 0.0001f);
    return MoonGlow.rgb * pow(falloff, 1.5f);
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

float4 MainPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    float3 ray = normalize(rayIn);
    float enter;
    float stride;
    float reach;
    Span(ray, enter, stride, reach);
    float dither = PixelNoise(screen);

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
    float3 horizon = FogColour.rgb + FogColourRange.rgb * fogHeading;

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

            float height = saturate((at.z - Layer.x) / Layer.y);
            float3 light = (Scatter(lit, lobes) * thin +
                            BackColour.rgb * lit * saturate(-cosAngle)) * above +
                           AmbientColour.rgb * lerp(1.0f - AmbientColour.w, 1.0f, height);

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

// How much of a ray the clouds cover, as MainPS draws them, with the march offset by `dither`.
float Cover(float3 rayIn, float dither) {
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
        float density = Density(at, false);
        if (density > 0.0005f) {
            transmittance *= exp(-density * stride);
        }
    }
    float cover = (1.0f - transmittance) * reach;

    float cirrusCover = 0.0f;
    HighCloud(ray, dot(ray, Light.xyz), 0.0f, cirrusCover);
    return cover + cirrusCover * (1.0f - cover);
}

// Discards a pixel with the likelihood that the clouds cover its ray.
float4 CoverPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    float dither = PixelNoise(screen);
    clip(dither - Cover(rayIn, dither));
    return 0.0f;
}

// What the clouds cover of a pixel's ray, for the god-ray mask to be multiplied down by.
float4 MaskPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    return Cover(rayIn, PixelNoise(screen));
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
