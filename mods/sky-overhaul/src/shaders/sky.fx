// The sky, drawn in place of the engine's own dome.
//
// The clouds are drawn over a finished pass; this is drawn inside one, where the dome's own draw
// was dropped. So it inherits the dome's state rather than setting any: depth tested against the
// world and not written, no culling, and blended source alpha over one minus source alpha. That
// blend is straight rather than premultiplied - the opposite of the cloud layer's convention - so
// the colour here is the sky's own and the alpha is how much of it covers what was drawn behind.
//
// Which matters most at night. The stars and the moon are drawn before the dome, not after, and it
// is the dome's alpha that lets them through: opaque by day, and by night only as bright as the
// sky itself is.
//
// The colour is not painted. It is what is left of sunlight after it has been scattered by the air
// between the eye and space, which is why dawn is red and noon is blue without either being
// written down anywhere.

// The air, in metres. A planet, because the whole point of a horizon is that the ground curves
// away from it - a flat atmosphere has no sunset, only a light that switches off.
#define PLANET_RADIUS 6371000.0f
#define ATMOSPHERE_RADIUS 6471000.0f

// How fast each kind of scattering thins out with height. Air itself is well mixed and reaches
// high; the dust and water it carries settle much lower, which is why haze is a horizon effect.
#define RAYLEIGH_HEIGHT 8000.0f
#define MIE_HEIGHT 1200.0f

// How much of a metre of sea-level air a colour is scattered by. Blue is scattered nearly six
// times as hard as red, and that ratio alone is the whole colour of the sky.
#define RAYLEIGH_BETA float3(5.802e-6f, 13.558e-6f, 33.100e-6f)
// Haze scatters every colour alike, so it whitens rather than tints. It absorbs as well, so what
// it takes out of a ray is more than what it sends on.
#define MIE_BETA 3.996e-6f
#define MIE_EXTINCTION 4.440e-6f

// How hard light bends forward off a haze droplet. Deliberately short of the value the physics
// would like: the engine still draws its own sun disc and flare over this, and a sharper lobe
// would put a second sun around the first.
#define MIE_FORWARD 0.70f

// How many samples the view ray takes through the air, and how many each of those takes toward the
// sun to find out how much light got that far.
#define VIEW_STEPS 16
#define LIGHT_STEPS 8

#define PI 3.14159265f

// How high up the engine's own fog reads our sky, in metres. The engine measures its fog along the
// dome's own mesh, whose shape is not written down anywhere; this is the height the clouds are
// already faded by, so the two of them meet the horizon together.
#define SKY_HEIGHT 180.0f

// xyz: where the camera is, in world space with Z up. w: the frame's exposure.
float4 Eye : register(c71);
// xyz: the direction of the sun, pointing at it. w: zero in daylight, one at night.
float4 Sun : register(c72);
// x: how much haze the air carries. y: how bright the sun is. Both finished values, with the
// weather already folded in.
float4 Air : register(c73);

// The engine's own sky fog, register for register, so our sky meets the terrain in the colour the
// terrain fades to. See docs/docs/engine-internals/sky-and-clouds.md.
float4 FogColour : register(c74);
float4 FogColourRange : register(c75);
float4 FogValues : register(c76);
float4 FogHeightValues : register(c77);
float4 FogColourVector : register(c78);

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

// How far a ray from inside a sphere runs before it leaves it.
float SphereExit(float3 origin, float3 ray, float radius) {
    float along = dot(origin, ray);
    float outside = dot(origin, origin) - radius * radius;
    float discriminant = along * along - outside;
    return discriminant < 0.0f ? 0.0f : -along + sqrt(discriminant);
}

// Whether the planet stands between a point and the sun, which is the difference between dusk and
// a light that goes out all at once.
bool InShadow(float3 origin, float3 ray) {
    float along = dot(origin, ray);
    float outside = dot(origin, origin) - PLANET_RADIUS * PLANET_RADIUS;
    float discriminant = along * along - outside;
    return discriminant >= 0.0f && -along - sqrt(discriminant) > 0.0f;
}

// How much air a ray toward the sun passes through, counted separately for the two kinds of it.
float2 SunDepth(float3 from, float3 sun) {
    float stride = SphereExit(from, sun, ATMOSPHERE_RADIUS) / LIGHT_STEPS;
    float2 depth = 0.0f;
    [unroll] for (int i = 0; i < LIGHT_STEPS; i++) {
        float3 at = from + sun * (((float)i + 0.5f) * stride);
        float height = length(at) - PLANET_RADIUS;
        depth += exp(-height / float2(RAYLEIGH_HEIGHT, MIE_HEIGHT)) * stride;
    }
    return depth;
}

// What one look through the air comes back with: sunlight scattered into the eye once, from every
// point along the ray that can still see the sun.
float3 Scattered(float3 ray, float3 sun, float mie, float intensity) {
    float3 origin = float3(0.0f, 0.0f, PLANET_RADIUS + Eye.z);
    float stride = SphereExit(origin, ray, ATMOSPHERE_RADIUS) / VIEW_STEPS;

    float2 depth = 0.0f;
    float3 rayleighSum = 0.0f;
    float3 mieSum = 0.0f;

    [loop] for (int i = 0; i < VIEW_STEPS; i++) {
        float3 at = origin + ray * (((float)i + 0.5f) * stride);
        float height = length(at) - PLANET_RADIUS;
        float2 density = exp(-height / float2(RAYLEIGH_HEIGHT, MIE_HEIGHT)) * stride;
        depth += density;

        if (!InShadow(at, sun)) {
            float2 sunDepth = SunDepth(at, sun);
            // What is left of the sunlight after reaching this point, and of the scattered light
            // after coming back to the eye.
            float3 survives = exp(-(RAYLEIGH_BETA * (depth.x + sunDepth.x) +
                                    (MIE_EXTINCTION * mie) * (depth.y + sunDepth.y)));
            rayleighSum += survives * density.x;
            mieSum += survives * density.y;
        }
    }

    // How much of what it scatters each kind sends in the direction the eye happens to be looking.
    // Air sends it nearly everywhere; haze throws most of it forward, which is the glare around a
    // low sun.
    float cosAngle = dot(ray, sun);
    float rayleighPhase = 3.0f / (16.0f * PI) * (1.0f + cosAngle * cosAngle);
    float g = MIE_FORWARD;
    float miePhase = 3.0f / (8.0f * PI) * ((1.0f - g * g) * (1.0f + cosAngle * cosAngle)) /
                     ((2.0f + g * g) * pow(max(1.0f + g * g - 2.0f * g * cosAngle, 0.0001f), 1.5f));

    return intensity * (RAYLEIGH_BETA * rayleighPhase * rayleighSum +
                        (MIE_BETA * mie) * miePhase * mieSum);
}

float4 MainPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    float3 ray = normalize(rayIn);

    // Below the horizon there is no sky, only ground the world has already drawn over - so those
    // rays take the horizon's own colour rather than marching off into the planet.
    float3 skyward = normalize(float3(ray.xy, max(ray.z, 0.0f)));
    float3 colour = Scattered(skyward, Sun.xyz, Air.x, Air.y);

    // Where the sky ends up at the horizon: the engine's own colour, by heading against the sun,
    // so that the terrain fading into it and the sky arriving at it meet in one place.
    float heading =
        acos(clamp(dot(normalize(ray.xy + 0.0001f), FogColourVector.xy), -1.0f, 1.0f)) / PI;
    float3 horizon = FogColour.rgb + FogColourRange.rgb * heading;

    float fogHeight = saturate(ray.z * SKY_HEIGHT * FogHeightValues.x + FogHeightValues.y);
    float fog = saturate((fogHeight * FogHeightValues.z + FogHeightValues.w) * FogValues.z);
    colour = lerp(colour, horizon, fog);

    // The dome's own alpha, taken before the exposure as the dome takes it: opaque while there is
    // daylight, and at night only as much as the sky is bright, which is what lets the stars
    // through.
    float luminance = dot(colour, float3(0.299f, 0.587f, 0.114f));
    float alpha = saturate((1.0f - Sun.w) + luminance * 0.5f);

    // Half the frames are eight bits a channel, and a sky is the one thing in a game made entirely
    // of gradients - so the last bit is broken up on purpose, which is cheaper than any number of
    // extra bits would be.
    float dither = frac(52.9829189f * frac(0.06711056f * screen.x + 0.00583715f * screen.y));
    colour += (dither - 0.5f) / 255.0f;

    return float4(colour * Eye.w, alpha);
}
