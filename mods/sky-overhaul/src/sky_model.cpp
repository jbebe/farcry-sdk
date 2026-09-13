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

    // The storm factor's resting level.
    constexpr float kStormCalm = 0.2f;
    // How much haze fair air carries, and what a full storm does to the air: more haze, far less of
    // the sun reaching it, and its colour drained most of the way to grey.
    constexpr float kClearHaze = 0.75f;
    constexpr float kStormHaze = 1.4f;
    constexpr float kStormDimming = 0.7f;
    constexpr float kStormGrey = 0.85f;

    // How high the moon climbs, as a sine, while its light comes up to full.
    constexpr float kMoonUp = 0.2f;

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

    // The share of each colour of sunlight that reaches `at`, measured from the planet's centre.
    void Lit(const float at[3], const float sun[3], float mie, float out[3]) {
        if (InShadow(at, sun)) {
            out[0] = 0.0f;
            out[1] = 0.0f;
            out[2] = 0.0f;
            return;
        }
        float depth[3];
        SunDepth(at, sun, depth);
        for (int channel = 0; channel < 3; channel++) {
            out[channel] = std::exp(-(kRayleighBeta[channel] * depth[0] +
                                      kMieExtinction * mie * depth[1] +
                                      kOzoneAbsorption[channel] * depth[2]));
        }
    }
}

float SkyOverhaul::SkyModel::Storminess(float storm) {
    return std::clamp((storm - kStormCalm) / (1.0f - kStormCalm), 0.0f, 1.0f);
}

float SkyOverhaul::SkyModel::Haze(float storminess) {
    return kClearHaze * (1.0f + storminess * kStormHaze);
}

float SkyOverhaul::SkyModel::SunShare(float storminess) {
    return 1.0f - storminess * kStormDimming;
}

float SkyOverhaul::SkyModel::MoonRise(float moonHeight) {
    return std::clamp(moonHeight / kMoonUp, 0.0f, 1.0f);
}

float SkyOverhaul::SkyModel::Grey(float storminess) {
    return storminess * kStormGrey;
}

void SkyOverhaul::SkyModel::Radiance(const float ray[3], const float sun[3], float eyeHeight,
                                     float mie, float intensity, float out[3]) {
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

        float lit[3];
        Lit(at, sun, mie, lit);

        for (int channel = 0; channel < 3; channel++) {
            const float extinction = kRayleighBeta[channel] * rayleighDensity +
                                     kMieExtinction * mie * mieDensity +
                                     kOzoneAbsorption[channel] * ozoneDensity;
            const float scattering = kRayleighBeta[channel] * rayleighDensity * rayleighPhase +
                                     kMieBeta * mie * mieDensity * miePhase;
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

void SkyOverhaul::SkyModel::Sunlight(const float sun[3], float height, float mie, float out[3]) {
    const float at[3] = {0.0f, 0.0f, kPlanetRadius + height};
    Lit(at, sun, mie, out);
}
