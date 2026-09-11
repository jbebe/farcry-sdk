#include "sky_model.h"

#include <algorithm>
#include <cmath>

namespace {
    constexpr float kPlanetRadius = 6371000.0f;
    constexpr float kAtmosphereRadius = 6471000.0f;
    constexpr float kRayleighHeight = 8000.0f;
    constexpr float kMieHeight = 1200.0f;
    constexpr float kRayleighBeta[3] = {5.802e-6f, 13.558e-6f, 33.100e-6f};
    constexpr float kMieBeta = 3.996e-6f;
    constexpr float kMieExtinction = 4.440e-6f;
    constexpr float kMieForward = 0.70f;
    constexpr float kOzoneAbsorption[3] = {0.650e-6f, 1.881e-6f, 0.085e-6f};
    constexpr float kOzoneCentre = 25000.0f;
    constexpr float kOzoneWidth = 15000.0f;
    constexpr int kViewSteps = 16;
    constexpr int kLightSteps = 8;
    constexpr float kPi = 3.14159265f;

    float Dot(const float a[3], const float b[3]) {
        return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    }

    float SphereExit(const float origin[3], const float ray[3], float radius) {
        const float along = Dot(origin, ray);
        const float outside = Dot(origin, origin) - radius * radius;
        const float discriminant = along * along - outside;
        return discriminant < 0.0f ? 0.0f : -along + std::sqrt(discriminant);
    }

    bool InShadow(const float origin[3], const float ray[3]) {
        const float along = Dot(origin, ray);
        const float outside = Dot(origin, origin) - kPlanetRadius * kPlanetRadius;
        const float discriminant = along * along - outside;
        return discriminant >= 0.0f && -along - std::sqrt(discriminant) > 0.0f;
    }

    float OzoneDensity(float height) {
        const float density = 1.0f - std::fabs(height - kOzoneCentre) / kOzoneWidth;
        return density > 0.0f ? density : 0.0f;
    }

    void SunDepth(const float from[3], const float sun[3], float out[3]) {
        const float stride = SphereExit(from, sun, kAtmosphereRadius) / kLightSteps;
        out[0] = 0.0f;
        out[1] = 0.0f;
        out[2] = 0.0f;
        for (int i = 0; i < kLightSteps; i++) {
            const float distance = (static_cast<float>(i) + 0.5f) * stride;
            const float at[3] = {from[0] + sun[0] * distance, from[1] + sun[1] * distance,
                                 from[2] + sun[2] * distance};
            const float height = std::sqrt(Dot(at, at)) - kPlanetRadius;
            out[0] += std::exp(-height / kRayleighHeight) * stride;
            out[1] += std::exp(-height / kMieHeight) * stride;
            out[2] += OzoneDensity(height) * stride;
        }
    }
}

void SkyOverhaul::SkyModel::Radiance(const float ray[3], const float sun[3], float eyeHeight,
                                     float mie, const float hazeColour[3], float intensity,
                                     float out[3]) {
    const float origin[3] = {0.0f, 0.0f, kPlanetRadius + eyeHeight};
    const float span = SphereExit(origin, ray, kAtmosphereRadius);

    const float cosAngle = Dot(ray, sun);
    const float rayleighPhase = 3.0f / (16.0f * kPi) * (1.0f + cosAngle * cosAngle);
    const float g = kMieForward;
    const float denominator = 1.0f + g * g - 2.0f * g * cosAngle;
    const float miePhase = 3.0f / (8.0f * kPi) * ((1.0f - g * g) * (1.0f + cosAngle * cosAngle)) /
                           ((2.0f + g * g) * std::pow(denominator > 0.0001f ? denominator : 0.0001f,
                                                      1.5f));

    // Samples crowded toward the eye, each stretch between them integrated exactly, as the shader
    // does and for the reason it gives.
    float through[3] = {1.0f, 1.0f, 1.0f};
    float gathered[3] = {0.0f, 0.0f, 0.0f};
    for (int i = 0; i < kViewSteps; i++) {
        const float inner = static_cast<float>(i) / kViewSteps;
        const float outer = static_cast<float>(i + 1) / kViewSteps;
        const float fromEye = span * inner * inner;
        const float toEye = span * outer * outer;
        const float stride = toEye - fromEye;
        const float middle = 0.5f * (fromEye + toEye);

        const float at[3] = {origin[0] + ray[0] * middle, origin[1] + ray[1] * middle,
                             origin[2] + ray[2] * middle};
        const float height = std::sqrt(Dot(at, at)) - kPlanetRadius;
        const float rayleighDensity = std::exp(-height / kRayleighHeight);
        const float mieDensity = std::exp(-height / kMieHeight);
        const float ozoneDensity = OzoneDensity(height);

        float lit[3] = {0.0f, 0.0f, 0.0f};
        if (!InShadow(at, sun)) {
            float sunDepth[3];
            SunDepth(at, sun, sunDepth);
            for (int channel = 0; channel < 3; channel++) {
                lit[channel] = std::exp(-(kRayleighBeta[channel] * sunDepth[0] +
                                          kMieExtinction * mie * sunDepth[1] +
                                          kOzoneAbsorption[channel] * sunDepth[2]));
            }
        }

        for (int channel = 0; channel < 3; channel++) {
            const float extinction = kRayleighBeta[channel] * rayleighDensity +
                                     kMieExtinction * mie * mieDensity +
                                     kOzoneAbsorption[channel] * ozoneDensity;
            const float scattering = kRayleighBeta[channel] * rayleighDensity * rayleighPhase +
                                     kMieBeta * mie * mieDensity * miePhase * hazeColour[channel];
            const float transmit = std::exp(-extinction * stride);
            const float safe = extinction > 1.0e-12f ? extinction : 1.0e-12f;
            gathered[channel] += through[channel] * scattering * lit[channel] * (1.0f - transmit) / safe;
            through[channel] *= transmit;
        }
    }

    for (int channel = 0; channel < 3; channel++) {
        out[channel] = intensity * gathered[channel];
    }
}

void SkyOverhaul::SkyModel::Horizon(const float heading[3], const float sun[3], float eyeHeight,
                                    float mie, float intensity, const Tuning::Values& tuning,
                                    float out[3]) {
    Radiance(heading, sun, eyeHeight, mie, tuning.hazeColour, intensity, out);

    // The shader's Farness, which is half all the way round a sun straight up or down.
    const float across = std::sqrt(sun[0] * sun[0] + sun[1] * sun[1]);
    float farness = 0.5f;
    if (across >= 0.0001f) {
        const float facing = (heading[0] * sun[0] + heading[1] * sun[1]) / across;
        const float t = std::clamp(0.5f - 0.5f * facing, 0.0f, 1.0f);
        farness = t * t * (3.0f - 2.0f * t);
    }

    for (int channel = 0; channel < 3; channel++) {
        const float nearColour = tuning.nearHorizonColour[channel] * tuning.nearHorizonBrightness;
        const float horizonColour =
            nearColour + (tuning.farHorizonColour[channel] - nearColour) * farness;
        out[channel] *= tuning.skyColour[channel] * horizonColour;
    }
}
