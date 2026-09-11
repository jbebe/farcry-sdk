// Every value the sky, the clouds and the glare are tuned by: set per key moment of the sun's day or
// for the whole day, kept in bin\sky-overhaul.ini and edited in DevTools' overlay.
#pragma once

namespace SkyOverhaul::Tuning {

// Every tunable value, in the units the effects use. Floats only, so the table in tuning.cpp
// addresses each by offset.
struct Values {
    float skyHaze;
    float hazeColour[3];
    float skyBrightness;
    float skyColour[3];
    float zenithHold;
    float horizonGradient;
    float nearHorizonColour[3];
    float nearHorizonBrightness;
    float farHorizonColour[3];
    float farHorizonBrightness;
    float horizonMatch;
    float groundBrown;
    float groundColour[3];
    float nightSky;
    float nightSkyColour[3];
    float twilightSky;
    float twilightColour[3];

    float cloudCoverage;
    float cloudDensity;
    float cloudDetail;
    float cloudHaze;
    float cirrus;
    float cirrusOpacity;
    float contrails;
    float moonlight;
    float moonColour[3];
    float moonGlow;

    float cloudBase;
    float cloudThickness;
    float cloudSize;
    float cloudWind;

    float glareStrength;
    float glareSpread;
    float glareFalloff;
    float glareVeil;
    float glareContrast;
    float glareDesaturation;
    float elevationRamp;

    float afterimageStrength;
    float afterimageSeconds;
    float afterimageDarkness;
    float afterimageTint;
    float afterimageSaturation;
    float afterimageSize;
    float afterimageHaze;
};

// What the effects draw with at a time-of-day coordinate: the whole day's values, with every
// per-moment value blended between the key moments either side of it.
Values Evaluate(float timeOfDay);

// Reads bin\sky-overhaul.ini over the defaults and writes the file back complete, which is also how
// it first appears.
void Load();

// Draws the window in DevTools' overlay.
void DrawWindow(void* userData);

}
