// Every value the clouds, the glare, the night, the sun and cloud shadows, the ambient occlusion,
// the grade, the grass and the leaves are tuned by, kept in bin\sky-overhaul.ini and edited in
// DevTools' overlay.
#pragma once

namespace SkyOverhaul::Tuning {

// Every tunable value. Floats only, so the table in tuning.cpp addresses each by offset.
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

    float duskFogBrightness;

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

    float sunShadowStartAngle;
    float sunShadowEndAngle;
    float sunShadowRange0;
    float sunShadowRange1;
    float sunShadowRange2;

    float shadowStrength;

    float occlusionStrength;
    float occlusionRadius;
    float occlusionScreenLimit;
    float occlusionFadeDistance;
    float occlusionRootReach;

    float gradeSaturation;
    float gradeContrast;
    float gradeBrightness;
    float gradeWarmth;
    float gradeTint;

    float grassRootShade;
    float grassSideLight;
    float grassSheen;
    float grassSheenNarrowness;
    float grassGlow;

    float leafCrownShade;
    float leafCrownThickness;
    float leafTilt;
    float leafGlint;
    float leafGlow;
};

// What the effects draw with.
Values Current();

// Reads bin\sky-overhaul.ini over the defaults and writes the file back complete, which is also how
// it first appears.
void Load();

// Draws the window in DevTools' overlay.
void DrawWindow(void* userData);

}
