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
float4 Range : register(c79);

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

// Flat at the bottom and rounded off at the top, which is the profile that reads as cumulus. The
// weather map's own height field stretches or squashes it.
float HeightProfile(float height, float tallness) {
    float top = lerp(0.35f, 1.0f, tallness);
    return saturate(Remap(height, 0.0f, 0.15f * top, 0.0f, 1.0f)) *
           saturate(Remap(height, 0.6f * top, top, 1.0f, 0.0f));
}

// How much cloud stands at a point. `cheap` skips the erosion, which is most of the cost and none
// of the shape, and is what the samples toward the sun use.
float Density(float3 world, uniform bool cheap) {
    float height = saturate((world.z - Layer.x) / Layer.y);

    float2 weatherUv = (world.xy + Wind.zw) * Grain.z;
    float3 weather = tex2Dlod(WeatherMap, float4(weatherUv, 0.0f, 0.0f)).rgb;
    // Half is neutral, so the slider opens the sky out from the map or closes it down to nothing.
    float cloudiness = saturate(weather.b + Layer.z * 2.0f - 1.0f);

    float3 shapeUv = (world + float3(Wind.xy, 0.0f)) * Grain.x;
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

// Two lobes: most light carries on forward past a droplet, a little of it comes back.
float Phase(float cosAngle, float forward) {
    float squared = forward * forward;
    float lobe = (1.0f - squared) / pow(1.0f + squared - 2.0f * forward * cosAngle, 1.5f);
    return lerp(0.25f, lobe * 0.25f, 0.7f);
}

// How much of the sun reaches a point, by marching toward it and counting what is in the way.
float SunReach(float3 world) {
    float depth = 0.0f;
    [unroll] for (int i = 0; i < LIGHT_STEPS; i++) {
        float3 at = world + Sun.xyz * ((float)i + 0.5f) * SunColour.w;
        depth += Density(at, true);
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

    float reach = saturate((Range.x - enter) / Range.y) * step(0.001f, ray.z) * step(enter, leave);
    float span = max(leave - enter, 0.0f);
    float stride = span / VIEW_STEPS;

    // A different offset per pixel, so that what the coarse sampling misses lands as fine noise
    // instead of as bands. Fixed to the pixel rather than the frame: the game has nothing that
    // would blend a moving pattern away, so a moving one would only flicker.
    float dither = frac(52.9829189f * frac(0.06711056f * screen.x + 0.00583715f * screen.y));

    float cosAngle = dot(ray, Sun.xyz);
    float phase = Phase(cosAngle, Sun.w);

    float transmittance = 1.0f;
    float3 scattered = 0.0f;
    [loop] for (int i = 0; i < VIEW_STEPS; i++) {
        if (transmittance < 0.01f) {
            break;
        }
        float3 at = Eye.xyz + ray * (enter + ((float)i + dither) * stride);
        float density = Density(at, false);
        if (density > 0.0005f) {
            float lit = SunReach(at);

            // Dark where the cloud is thin and the light has not yet scattered into it, which is
            // the shading that keeps an edge from looking like paper.
            float powder = 1.0f - exp(-density * 8.0f);

            float3 light = SunColour.rgb * lit * phase * powder +
                           BackColour.rgb * lit * saturate(-cosAngle) + AmbientColour.rgb;

            float transmit = exp(-density * stride);
            scattered += light * transmittance * (1.0f - transmit);
            transmittance *= transmit;
        }
    }

    // The sky's fog, which is a function of where the ray points rather than how far it went: the
    // colour by heading against the sun, the amount by height up the dome the engine draws.
    float fogHeading = acos(clamp(dot(normalize(ray.xy + 0.0001f), FogColourVector.xy), -1.0f,
                                  1.0f)) / 3.14157f;
    float3 haze = FogColour.rgb + FogColourRange.rgb * fogHeading;
    float fogHeight = saturate(ray.z * 180.0f * FogHeightValues.x + FogHeightValues.y);
    float fog = saturate((fogHeight * FogHeightValues.z + FogHeightValues.w) * FogValues.z);

    float cover = (1.0f - transmittance) * reach;
    float3 colour = lerp(scattered * reach, haze * cover, fog);
    return float4(colour * Eye.w, 1.0f - cover);
}
