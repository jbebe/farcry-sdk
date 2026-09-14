// Every value the clouds, the glare, the night, the cloud shadows and the grade are tuned by, kept
// in bin\sky-overhaul.ini and edited in DevTools' overlay.
#pragma once

namespace SkyOverhaul::Tuning {

// Every tunable value, in the units the effects use. Floats only, so the table in tuning.cpp
// addresses each by offset.
struct Values {
    float cloudCoverage;
    float cloudDensity;
    float cloudDetail;
    float cloudHaze;
    float cirrus;
    float cirrusOpacity;
    float contrails;

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

    float nightStrength;
    float nightColourAbove;
    float nightPurkinje;
    float nightMoonColour;
    float nightNoise;

    float shadowStrength;

    float gradeSaturation;
    float gradeContrast;
    float gradeRed;
    float gradeGreen;
    float gradeBlue;
};

// What the effects draw with.
Values Current();

// Reads bin\sky-overhaul.ini over the defaults and writes the file back complete, which is also how
// it first appears.
void Load();

// Draws the window in DevTools' overlay.
void DrawWindow(void* userData);

}
