// The clouds, drawn over the world's own sky pass in place of the engine's.
//
// The output convention is the engine's own cloud layer's, so that the bloom and tone mapping
// after it see what they saw before: colour premultiplied and scaled by the exposure, alpha the
// transmittance, blended with (one, source alpha).

// How many samples the view ray takes through the layer, and how many the light ray takes toward
// the sun from each of those. Fixed rather than a constant so the loops unroll and the shader
// needs no integer registers.
#define VIEW_STEPS 48
#define LIGHT_STEPS 5

// How far apart two samples may be before the march starts stepping over whole clouds. A ray near
// the horizon runs almost along the layer and would otherwise spread its samples over kilometres.
#define MAX_STRIDE 90.0f

// How much light the high sheet bends forward. Gentle: enough that it lifts toward the sun, not
// so much that it only exists there.
#define CIRRUS_FORWARD 0.35f

// How much of the sun the sheet returns. Ice scatters more of what reaches it than water does, and
// there is no depth for any of it to be lost in.
#define CIRRUS_ALBEDO 1.1f

// Where the two aircraft went: a unit normal and how far the line sits from the origin, in the
// units the sheet is read in. Fixed, because they are scenery rather than traffic.
#define TRAIL_A float3(0.60f, 0.80f, 0.22f)
#define TRAIL_B float3(-0.94f, 0.34f, -0.55f)

// How deep the hazy air under the sheet effectively is, in metres, which is the distance a ray
// straight up spends in it. Everything below is that same depth divided by how slanted the ray is.
#define CIRRUS_AIR 900.0f

// How many texels across one repeat each volume holds, which is what turns a sample spacing into
// the level of the noise that matches it.
#define SHAPE_TEXELS 128.0f
#define DETAIL_TEXELS 32.0f

// The noise is read at its finest level wherever it is sampled. The volumes carry coarser levels
// too, one for each halving of the sample spacing, but taking them costs more shape than it saves
// grain: a cloud loses its bite long before it stops sparkling. The haze below does that work
// instead, by turning a distant cloud into distant air rather than into a smoother cloud.
#define NoiseLevel(stride, grain, texels) 0.0f

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
// xyz: the direction of the sun. w: how much light bends forward off a droplet.
float4 Sun : register(c75);
// rgb: the sun's colour. w: how far apart the samples toward it are.
float4 SunColour : register(c76);
// rgb: what the sky and ground light the cloud with from every other direction.
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

// The engine's own sky fog, register for register, so our clouds sit in the same haze the dome
// does. See docs/docs/engine-internals/sky-and-clouds.md.
float4 FogColour : register(c80);
float4 FogColourRange : register(c81);
float4 FogValues : register(c82);
float4 FogHeightValues : register(c83);
float4 FogColourVector : register(c84);

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
// of the shape, and is what the samples toward the sun use.
float Density(float3 world, float stride, uniform bool cheap) {
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
    float4 shape =
        tex3Dlod(ShapeNoise, float4(shapeUv, NoiseLevel(stride, Grain.x, SHAPE_TEXELS)));

    // Three frequencies of billow, folded into one field, then used to carve the fourth.
    float billow = shape.g * 0.625f + shape.b * 0.25f + shape.a * 0.125f;
    float body = saturate(Remap(shape.r, billow - 1.0f, 1.0f, 0.0f, 1.0f));
    body *= HeightProfile(height, weather.r);

    float density = saturate(Remap(body, 1.0f - cloudiness, 1.0f, 0.0f, 1.0f));

    if (cheap || density <= 0.0f) {
        return density * Layer.w;
    }

    // Wispy at the base and billowy above it, which is how a real cloud's edge tears. Read at
    // whatever level of the noise is as fine as the samples are apart: asking for more than that
    // would return a different answer at every pixel and read as grain rather than as an edge.
    float3 detail =
        tex3Dlod(DetailNoise, float4(world * Grain.y, NoiseLevel(stride, Grain.y, DETAIL_TEXELS)))
            .rgb;
    float fine = detail.r * 0.625f + detail.g * 0.25f + detail.b * 0.125f;
    float erosion = lerp(1.0f - fine, fine, saturate(height * 4.0f));
    density = saturate(Remap(density, erosion * Grain.w, 1.0f, 0.0f, 1.0f));
    return density * Layer.w;
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
float3 Scatter(float lit, float cosAngle) {
    float total = 0.0f;
    float brightness = 1.0f;
    float depth = 1.0f;
    float forward = Sun.w;
    [unroll] for (int order = 0; order < 3; order++) {
        total += Phase(cosAngle, forward) * pow(lit, depth) * brightness;
        brightness *= 0.55f;
        depth *= 0.5f;
        forward *= 0.5f;
    }
    return SunColour.rgb * total;
}

// What one aircraft left behind: a straight line at the sheet's altitude, narrow, and neither
// uniform nor endless. `path` is the line's unit normal and how far it sits from the origin, in
// the same units the sheet is read in.
float Contrail(float2 at, float3 path) {
    float across = abs(dot(at, path.xy) - path.z);
    float along = dot(at, float2(-path.y, path.x));

    // Whether there is a trail here at all, over a long wavelength. One aircraft passed once, and
    // what it left has been spreading and tearing apart ever since, so a trail arrives in lengths
    // rather than running unbroken from one horizon to the other.
    float presence = tex3Dlod(ShapeNoise, float4(along * Trail.z, 0.71f, 0.29f, 0.0f)).r;
    presence = saturate(Remap(presence, 0.42f, 0.72f, 0.0f, 1.0f));

    // Wider where it is older, which is also where it is fainter: a trail does not end, it spreads
    // until it is no longer a line.
    float spread = Trail.y * (1.0f + (1.0f - presence) * 2.5f);
    float core = saturate(1.0f - across / spread);
    return core * core * presence;
}

// The high sheet. Ice rather than water, thin enough that the sun passes almost straight through
// it, so there is no depth to march and it costs one intersection and one sample.
//
// The shape is layered noise taken as it comes, the way a paint program's cloud filter makes it:
// nothing stretched, folded or warped. How much of the field survives is one control and how
// solid what survives is is another, because they are two different things about a sky.
//
// It hazes on how slanted the view is rather than how far it went, because the air that does the
// hazing is all near the ground: six kilometres straight up passes through very little of it, and
// the same six kilometres along the horizon passes through nothing else.
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
    float trails = Contrail(at, TRAIL_A) + Contrail(at, TRAIL_B);
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
        SunColour.rgb * Phase(cosAngle, CIRRUS_FORWARD) * CIRRUS_ALBEDO + AmbientColour.rgb;
    return lerp(light, horizon, lost);
}

// How much of the sun reaches a point, by marching toward it and counting what is in the way.
float SunReach(float3 world) {
    float depth = 0.0f;
    [unroll] for (int i = 0; i < LIGHT_STEPS; i++) {
        float3 at = world + Sun.xyz * ((float)i + 0.5f) * SunColour.w;
        depth += Density(at, 0.0f, true);
    }
    return exp(-depth * SunColour.w);
}

float4 MainPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    float3 ray = normalize(rayIn);

    // Where the ray is inside the layer. Below the horizon, or above a layer the camera has
    // climbed over, there is nothing to march.
    float up = max(ray.z, 0.0001f);
    float toFloor = (Layer.x - Eye.z) / up;
    float toCeiling = (Layer.x + Layer.y - Eye.z) / up;
    float enter = max(min(toFloor, toCeiling), 0.0f);
    float leave = min(max(toFloor, toCeiling), Range.x);

    // The march covers only the near part of the layer. Spreading the same samples over the whole
    // of it would step clean past clouds near the horizon, where the ray runs almost along the
    // layer, and the shape would repeat visibly out there in any case.
    leave = min(leave, enter + Range.z);

    float reach = saturate((Range.x - enter) / Range.y) * step(0.001f, ray.z) * step(enter, leave);
    float span = max(leave - enter, 0.0f);
    float stride = min(span / VIEW_STEPS, MAX_STRIDE);

    // A different offset per pixel, so that what the coarse sampling misses lands as fine noise
    // instead of as bands. Fixed to the pixel rather than the frame: the game has nothing that
    // would blend a moving pattern away, so a moving one would only flicker.
    float dither = frac(52.9829189f * frac(0.06711056f * screen.x + 0.00583715f * screen.y));

    float cosAngle = dot(ray, Sun.xyz);

    // The colour the sky goes toward at the horizon, which is where everything below ends up: the
    // engine's own, by heading against the sun, so it matches the dome rather than approximating
    // it. Worked out before either layer, because both fade into it.
    float fogHeading =
        acos(clamp(dot(normalize(ray.xy + 0.0001f), FogColourVector.xy), -1.0f, 1.0f)) / 3.14157f;
    float3 horizon = FogColour.rgb + FogColourRange.rgb * fogHeading;

    float cirrusCover = 0.0f;
    float3 cirrus = HighCloud(ray, cosAngle, horizon, cirrusCover);

    float transmittance = 1.0f;
    float3 scattered = 0.0f;
    [loop] for (int i = 0; i < VIEW_STEPS; i++) {
        if (transmittance < 0.01f) {
            break;
        }
        float3 at = Eye.xyz + ray * (enter + ((float)i + dither) * stride);
        float density = Density(at, stride, false);
        if (density > 0.0005f) {
            float lit = SunReach(at);

            // Dark where the cloud is thin and the light has not yet scattered into it. Only worth
            // anything looking toward the sun, where it is what keeps a bright edge from reading
            // as paper; away from the sun it would only make an already dim cloud dimmer.
            float powder = 1.0f - exp(-density * 8.0f);
            float thin = lerp(1.0f, powder, saturate(cosAngle));

            float3 light = Scatter(lit, cosAngle) * thin +
                           BackColour.rgb * lit * saturate(-cosAngle) + AmbientColour.rgb;

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

    return float4(colour * Eye.w, 1.0f - cover);
}
