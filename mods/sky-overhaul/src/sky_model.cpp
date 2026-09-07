#include "sky_model.h"

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

    void SunDepth(const float from[3], const float sun[3], float out[2]) {
        const float stride = SphereExit(from, sun, kAtmosphereRadius) / kLightSteps;
        out[0] = 0.0f;
        out[1] = 0.0f;
        for (int i = 0; i < kLightSteps; i++) {
            const float distance = (static_cast<float>(i) + 0.5f) * stride;
            const float at[3] = {from[0] + sun[0] * distance, from[1] + sun[1] * distance,
                                 from[2] + sun[2] * distance};
            const float height = std::sqrt(Dot(at, at)) - kPlanetRadius;
            out[0] += std::exp(-height / kRayleighHeight) * stride;
            out[1] += std::exp(-height / kMieHeight) * stride;
        }
    }
}

void SkyOverhaul::SkyModel::Radiance(const float ray[3], const float sun[3], float eyeHeight,
                                     float mie, float intensity, float out[3]) {
    const float origin[3] = {0.0f, 0.0f, kPlanetRadius + eyeHeight};
    const float stride = SphereExit(origin, ray, kAtmosphereRadius) / kViewSteps;

    float depth[2] = {0.0f, 0.0f};
    float rayleighSum[3] = {0.0f, 0.0f, 0.0f};
    float mieSum[3] = {0.0f, 0.0f, 0.0f};

    for (int i = 0; i < kViewSteps; i++) {
        const float distance = (static_cast<float>(i) + 0.5f) * stride;
        const float at[3] = {origin[0] + ray[0] * distance, origin[1] + ray[1] * distance,
                             origin[2] + ray[2] * distance};
        const float height = std::sqrt(Dot(at, at)) - kPlanetRadius;
        const float density[2] = {std::exp(-height / kRayleighHeight) * stride,
                                  std::exp(-height / kMieHeight) * stride};
        depth[0] += density[0];
        depth[1] += density[1];

        if (InShadow(at, sun)) {
            continue;
        }
        float sunDepth[2];
        SunDepth(at, sun, sunDepth);
        for (int channel = 0; channel < 3; channel++) {
            const float survives =
                std::exp(-(kRayleighBeta[channel] * (depth[0] + sunDepth[0]) +
                           kMieExtinction * mie * (depth[1] + sunDepth[1])));
            rayleighSum[channel] += survives * density[0];
            mieSum[channel] += survives * density[1];
        }
    }

    const float cosAngle = Dot(ray, sun);
    const float rayleighPhase = 3.0f / (16.0f * kPi) * (1.0f + cosAngle * cosAngle);
    const float g = kMieForward;
    const float denominator = 1.0f + g * g - 2.0f * g * cosAngle;
    const float miePhase = 3.0f / (8.0f * kPi) * ((1.0f - g * g) * (1.0f + cosAngle * cosAngle)) /
                           ((2.0f + g * g) * std::pow(denominator > 0.0001f ? denominator : 0.0001f,
                                                      1.5f));

    for (int channel = 0; channel < 3; channel++) {
        out[channel] = intensity * (kRayleighBeta[channel] * rayleighPhase * rayleighSum[channel] +
                                    kMieBeta * mie * miePhase * mieSum[channel]);
    }
}
