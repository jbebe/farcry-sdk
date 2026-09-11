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

// How strongly ozone absorbs each colour per metre where it is densest, and where that is: a layer
// centred twenty-five kilometres up and gone fifteen either side. It takes green and red out of light
// that has come a long way through the upper air, which is what keeps a dawn zenith blue.
#define OZONE_ABSORPTION float3(0.650e-6f, 1.881e-6f, 0.085e-6f)
#define OZONE_CENTRE 25000.0f
#define OZONE_WIDTH 15000.0f

// How many samples the view ray takes through the air, and how many each of those takes toward the
// sun to find out how much light got that far.
#define VIEW_STEPS 16
#define LIGHT_STEPS 8

#define PI 3.14159265f
#define LUMA float3(0.299f, 0.587f, 0.114f)

// How tightly the engine's own horizon colour is kept to the horizon. Our air already pales the
// sky toward the bottom by itself, so this is here for one reason only: the last few degrees above
// the skyline have to be the colour the terrain fades into. Spread it any wider and the engine's
// colour washes the whole sky, which is exactly what the dome it came from used to do.
#define HORIZON_FALLOFF 8.0f

// Over what height of the sun the horizon's turn away from it fades out, as the sine of the sun's
// elevation: full strength below about eight degrees, gone by twenty-five. A high sun lights the
// horizon evenly all the way round, so noon is left exactly as it was.
#define GRADIENT_SUN_LOW 0.14f
#define GRADIENT_SUN_HIGH 0.42f

// How far up from the skyline that turn reaches. Wider than the engine's fog colour, so the band of
// sky above the skyline turns along with the skyline itself.
#define GRADIENT_FALLOFF 4.0f

// How far up the far side's darkening reaches, which is further than its change of hue: the air a
// low sun lights stands in a band well above the skyline, and the sky only goes dark near the
// zenith.
#define DIMMING_FALLOFF 1.5f

// How bright the far side of a low sun's horizon ends up, as a share of the light the air sends
// from there.
#define FAR_HORIZON_BRIGHTNESS 0.3f

// Below the horizon, over what depth of the sun the turn fades away, as the sine of its elevation:
// it stays through twilight and is gone by full night, which leaves the night sky and the stars
// exactly as they were.
#define GRADIENT_NIGHT_DEEP -0.21f
#define GRADIENT_NIGHT_EDGE -0.10f

// How far down from the zenith the hold on its brightness reaches, as a power of the ray's height:
// all of it overhead, half at forty-five degrees, next to none at the skyline, which is brighter in
// the afternoon than at noon already and needs nothing added.
#define ZENITH_HOLD_FALLOFF 2.0f

// Where the far side of a low sun takes the colour for everything at and below its horizon, as the
// sine of an elevation a few degrees up. The far side's darkening is deepest on the skyline itself,
// which would otherwise fill the ground the world never drew with something close to black.
#define BELOW_HORIZON_LIFT 0.07f

// Over how far below the horizon that ground turns brown, as a sine: fully by about three degrees.
#define BELOW_HORIZON_DEPTH 0.05f

// How far that ground turns brown at most, and dry earth at a brightness of one, so it can carry
// whatever brightness the horizon above it has.
#define GROUND_BROWN 0.5f
#define GROUND_HUE float3(1.236f, 0.939f, 0.692f)

// Over how much of the night factor, from its daylight end, the stars come out: the engine stops
// drawing them at zero, so they fade in that stretch rather than vanish at once.
#define STARS_FADE 0.02f

// The star sphere's backdrop, which is the colour of the night sky between the stars.
#define NIGHT_SKY float3(0.039f, 0.055f, 0.094f)

// The day sky's blue at a luminance of one, which that backdrop is lifted toward around sunrise and
// sunset, and how bright the lift is at its height.
#define TWILIGHT_HUE float3(0.50f, 1.04f, 2.11f)
#define TWILIGHT_LUMINANCE 0.08f
// The sun's heights, as sines, where that lift begins below the horizon and has gone above it.
#define TWILIGHT_DEEP -0.21f
#define TWILIGHT_END 0.105f

// The glow along the skyline toward a sun still below the horizon: orange at a luminance of one, how
// bright it is at its height, the sun's heights, as sines, where it begins and where it is brightest
// before sunrise takes over, and how tightly it hugs the skyline, as a power of the ray's height.
#define DAWN_GLOW_HUE float3(1.80f, 0.75f, 0.18f)
#define DAWN_GLOW_LUMINANCE 0.10f
#define DAWN_GLOW_DEEP -0.34f
#define DAWN_GLOW_PEAK -0.10f
#define DAWN_GLOW_FALLOFF 6.0f

// xyz: where the camera is, in world space with Z up. w: the frame's exposure.
float4 Eye : register(c71);
// xyz: the direction of the sun, pointing at it. w: zero in daylight, one at night.
float4 Sun : register(c72);
// x: how much haze the air carries. y: how bright the sun is. Both finished values, with the
// weather already folded in. z: how many times brighter than the air alone the zenith is drawn: one
// while the sun is overhead, more as it comes down, and back to one by sunset. w: how far a storm
// drains the sky's colour toward grey.
float4 Air : register(c73);

// The engine's own sky fog, register for register. See docs/docs/engine-internals/sky-and-clouds.md.
float4 FogColour : register(c74);
float4 FogColourRange : register(c75);
float4 FogColourVector : register(c76);

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

// How dense the ozone layer is at a height, from one at its centre to nothing at its edges.
float OzoneDensity(float height) {
    return saturate(1.0f - abs(height - OZONE_CENTRE) / OZONE_WIDTH);
}

// How much air, haze and ozone a ray toward the sun passes through, counted separately.
float3 SunDepth(float3 from, float3 sun) {
    float stride = SphereExit(from, sun, ATMOSPHERE_RADIUS) / LIGHT_STEPS;
    float3 depth = 0.0f;
    [unroll] for (int i = 0; i < LIGHT_STEPS; i++) {
        float3 at = from + sun * (((float)i + 0.5f) * stride);
        float height = length(at) - PLANET_RADIUS;
        depth += float3(exp(-height / float2(RAYLEIGH_HEIGHT, MIE_HEIGHT)), OzoneDensity(height)) *
                 stride;
    }
    return depth;
}

// What one look through the air comes back with: sunlight scattered into the eye once, from every
// point along the ray that can still see the sun.
//
// The samples crowd toward the eye, where a ray along the horizon gathers nearly all of its light,
// and the stretch of air between two of them is integrated exactly rather than treated as one
// point. A stretch near the horizon is thick enough to lose all of its blue before its far end, so
// charging that whole loss against its own light starves the blue and turns every horizon yellow,
// on both sides of the sky and at every hour. Against three thousand evenly spaced samples, this
// stays within a few percent at sixteen.
float3 Scattered(float3 ray, float3 sun, float mie, float intensity) {
    float3 origin = float3(0.0f, 0.0f, PLANET_RADIUS + Eye.z);
    float span = SphereExit(origin, ray, ATMOSPHERE_RADIUS);

    // How much of what it scatters each kind sends in the direction the eye happens to be looking.
    // Air sends it forward and back alike; haze throws most of it forward, which is the glow around
    // a low sun and the only thing that makes the sun's half of the sky brighter than the other.
    float cosAngle = dot(ray, sun);
    float rayleighPhase = 3.0f / (16.0f * PI) * (1.0f + cosAngle * cosAngle);
    float g = MIE_FORWARD;
    float miePhase = 3.0f / (8.0f * PI) * ((1.0f - g * g) * (1.0f + cosAngle * cosAngle)) /
                     ((2.0f + g * g) * pow(max(1.0f + g * g - 2.0f * g * cosAngle, 0.0001f), 1.5f));

    float3 through = 1.0f;
    float3 gathered = 0.0f;
    [loop] for (int i = 0; i < VIEW_STEPS; i++) {
        float inner = (float)i / VIEW_STEPS;
        float outer = (float)(i + 1) / VIEW_STEPS;
        float fromEye = span * inner * inner;
        float toEye = span * outer * outer;
        float stride = toEye - fromEye;

        float3 at = origin + ray * (0.5f * (fromEye + toEye));
        float height = length(at) - PLANET_RADIUS;
        float2 density = exp(-height / float2(RAYLEIGH_HEIGHT, MIE_HEIGHT));

        float3 extinction = RAYLEIGH_BETA * density.x + (MIE_EXTINCTION * mie) * density.y +
                            OZONE_ABSORPTION * OzoneDensity(height);
        float3 scattering = RAYLEIGH_BETA * (density.x * rayleighPhase) +
                            (MIE_BETA * mie * density.y * miePhase);

        // What is left of the sunlight after reaching this stretch of air.
        float3 lit = 0.0f;
        if (!InShadow(at, sun)) {
            float3 sunDepth = SunDepth(at, sun);
            lit = exp(-(RAYLEIGH_BETA * sunDepth.x + (MIE_EXTINCTION * mie) * sunDepth.y +
                        OZONE_ABSORPTION * sunDepth.z));
        }

        float3 transmit = exp(-extinction * stride);
        gathered += through * scattering * lit * (1.0f - transmit) / max(extinction, 1.0e-12f);
        through *= transmit;
    }

    return intensity * gathered;
}

// The engine's fog colour along a flat unit heading: its fog ramp, read by that heading's angle to
// its fog vector.
float3 FogAlong(float2 heading) {
    float angle = acos(clamp(dot(heading, FogColourVector.xy), -1.0f, 1.0f)) / PI;
    return FogColour.rgb + FogColourRange.rgb * angle;
}

// How far a heading belongs to the far side of the sun: nothing at the sun's own heading, half at a
// right angle to it, everything opposite, eased at both ends so neither side shows where the turn
// begins. Half all the way round a sun that is straight up or down.
float Farness(float2 heading) {
    float sunAcross = length(Sun.xy);
    if (sunAcross < 0.0001f) {
        return 0.5f;
    }
    return smoothstep(0.0f, 1.0f, 0.5f - 0.5f * dot(heading, Sun.xy / sunAcross));
}

// How far a ray at that farness takes the far side's horizon: none unless the sun is low.
float FarTurn(float farness) {
    float lowSun = (1.0f - smoothstep(GRADIENT_SUN_LOW, GRADIENT_SUN_HIGH, Sun.z)) *
                   smoothstep(GRADIENT_NIGHT_DEEP, GRADIENT_NIGHT_EDGE, Sun.z);
    return farness * lowSun;
}

// The horizon as the eye turns away from a low sun: bright and warm where the sun stands, growing
// darker and taking the hue the engine gives the far side of its fog as the eye comes round. Air
// alone lights the far side of a low sky nearly as brightly as the sun's side, which reads as light
// coming from where the earth's shadow ought to be.
float3 TurnFromSun(float3 colour, float below, float turn) {
    if (turn <= 0.0f) {
        return colour;
    }
    float hueAmount = turn * pow(below, GRADIENT_FALLOFF);
    float dimAmount = turn * pow(below, DIMMING_FALLOFF);

    // The fog's colour looking directly away from the sun, whichever end of its ramp that is: the
    // engine turns its fog vector round during the day and swaps the ramp's two ends with it.
    float3 farFog = FogAlong(-normalize(Sun.xy));

    // That colour at a brightness of one, so it can carry any brightness. Capped, because a fog
    // colour with next to no red or green in it would otherwise ask for several times the light.
    float3 farHue = min(farFog / max(dot(farFog, LUMA), 0.0001f), 3.0f);

    float3 turned = lerp(colour, dot(colour, LUMA) * farHue, hueAmount);
    return turned * lerp(1.0f, FAR_HORIZON_BRIGHTNESS, dimAmount);
}

// The darkest the sky is let go: the stars' own backdrop, lifted toward the day's blue from twelve
// degrees below the horizon to sunrise, and gone again six degrees above.
float3 NightFloor() {
    float twilight = smoothstep(TWILIGHT_DEEP, 0.0f, Sun.z) *
                     (1.0f - smoothstep(0.0f, TWILIGHT_END, Sun.z));
    return NIGHT_SKY + TWILIGHT_HUE * (twilight * TWILIGHT_LUMINANCE);
}

// The glow along the skyline toward a sun just below the horizon.
float3 DawnGlow(float farness, float below) {
    float rise = smoothstep(DAWN_GLOW_DEEP, DAWN_GLOW_PEAK, Sun.z) *
                 (1.0f - smoothstep(DAWN_GLOW_PEAK, 0.0f, Sun.z));
    float sunward = 1.0f - farness;
    return DAWN_GLOW_HUE *
           (DAWN_GLOW_LUMINANCE * rise * sunward * sunward * pow(below, DAWN_GLOW_FALLOFF));
}

float4 MainPS(float3 rayIn : TEXCOORD0, float2 screen : VPOS) : COLOR0 {
    float3 ray = normalize(rayIn);
    float2 heading = normalize(ray.xy + 0.0001f);
    float farness = Farness(heading);
    float turn = FarTurn(farness);

    // Below the horizon there is no sky, only ground: mostly drawn over by the world, and where it is
    // not, those rays take a horizon's colour rather than marching off into the planet. On the far
    // side of a low sun that is the colour from a few degrees up, since the skyline itself is where
    // the far side's darkening goes deepest.
    float3 skyward = normalize(float3(ray.xy, max(ray.z, BELOW_HORIZON_LIFT * turn)));
    float3 colour = Scattered(skyward, Sun.xyz, Air.x, Air.y);
    // One at the skyline, nought overhead.
    float below = saturate(1.0f - skyward.z);

    // As the sun comes down the air overhead loses nearly half its light while the sun's side and
    // the skyline gain more than that, and the frame's exposure does not move to meet either. Giving
    // light back toward the zenith alone keeps an afternoon sky blue without blowing out its horizon.
    colour *= lerp(1.0f, Air.z, pow(skyward.z, ZENITH_HOLD_FALLOFF));

    // Where the sky ends up at the horizon: the engine's own fog colour along this heading.
    float fog = pow(below, HORIZON_FALLOFF);
    colour = lerp(colour, FogAlong(heading), fog);
    colour = TurnFromSun(colour, below, turn);

    // What shows below that horizon is ground the world never drew, so it turns from the sky's
    // colour toward earth's the further down the eye goes, at the brightness the horizon already has.
    float ground = turn * saturate(-ray.z / BELOW_HORIZON_DEPTH) * GROUND_BROWN;
    colour = lerp(colour, dot(colour, LUMA) * GROUND_HUE, ground);

    // Air lit only once leaves the far side of a sun just below the horizon black, where the real sky
    // is still deep blue, so wherever the sky comes out darker than the night it is filled up to it.
    float3 darkest = NightFloor();
    float fill = saturate(1.0f - dot(colour, LUMA) / max(dot(darkest, LUMA), 0.0001f));
    colour += darkest * fill;

    colour += DawnGlow(farness, below);
    colour = lerp(colour, dot(colour, LUMA), Air.w);

    // Opaque by day, and at night only as much as the sky is bright, taken before the exposure as
    // the dome takes it. The dome's own alpha keeps covering the stars until the night factor is
    // one, which hides them for hours before dawn.
    float luminance = dot(colour, LUMA);
    float alpha = saturate(saturate(1.0f - Sun.w / STARS_FADE) + luminance * 0.5f);

    // Half the frames are eight bits a channel, and a sky is the one thing in a game made entirely
    // of gradients - so the last bit is broken up on purpose, which is cheaper than any number of
    // extra bits would be.
    float dither = frac(52.9829189f * frac(0.06711056f * screen.x + 0.00583715f * screen.y));
    colour += (dither - 0.5f) / 255.0f;

    return float4(colour * Eye.w, alpha);
}
