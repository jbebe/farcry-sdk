#include "weather/levers.h"

#include <algorithm>

namespace {
    float Mix(float a, float b, float t) {
        return a + (b - a) * t;
    }
}

SkyOverhaul::Weather::Levers SkyOverhaul::Weather::FromRow(const Row& row) {
    const float weight = row.fog > 0.0f ? 1.0f : 0.0f;
    return {row.coverage,
            row.density,
            row.tallness,
            row.shade,
            row.cirrus,
            row.cirrusOpacity,
            row.contrails,
            row.storminess,
            row.haze,
            row.heat,
            weight,
            row.fog * weight,
            {row.fogTint[0] * weight, row.fogTint[1] * weight, row.fogTint[2] * weight},
            0.0f,
            0.0f,
            row.rain,
            row.wind,
            row.dust,
            row.milk};
}

SkyOverhaul::Weather::Levers SkyOverhaul::Weather::Lerp(const Levers& from, const Levers& to,
                                                        float t) {
    Levers out;
    const float* a = reinterpret_cast<const float*>(&from);
    const float* b = reinterpret_cast<const float*>(&to);
    float* o = reinterpret_cast<float*>(&out);
    static_assert(sizeof(Levers) % sizeof(float) == 0);
    for (size_t i = 0; i < sizeof(Levers) / sizeof(float); i++) {
        o[i] = Mix(a[i], b[i], t);
    }
    return out;
}

void SkyOverhaul::Weather::AddFog(Levers& base, const Fog& fog, float amount) {
    if (amount <= 0.0f || fog.end <= 0.0f) {
        return;
    }
    // The nearer end wins, the weather's only as far as the fog is the weather's own, so nothing
    // jumps as the weather's fog fades out under the mist.
    const float own = FogOf(base).end;
    const float end = fog.end + (std::min)(own - fog.end, 0.0f) * base.fogWeight;
    base.fogReach = Mix(base.fogReach, end, amount);
    for (int c = 0; c < 3; c++) {
        base.fogTint[c] = Mix(base.fogTint[c], fog.tint[c], amount);
    }
    base.fogLow = Mix(base.fogLow, fog.low, amount);
    base.fogTop = Mix(base.fogTop, fog.top, amount);
    base.fogWeight = Mix(base.fogWeight, 1.0f, amount);
}

float SkyOverhaul::Weather::Cover(const Levers& levers) {
    return std::clamp(Mix(levers.coverage, 1.0f, levers.cirrus * levers.cirrusOpacity), 0.0f, 1.0f);
}

SkyOverhaul::Weather::Fog SkyOverhaul::Weather::FogOf(const Levers& levers) {
    const float weight = levers.fogWeight;
    if (weight <= 0.0f) {
        return {0.0f, {1.0f, 1.0f, 1.0f}, 0.0f, 0.0f};
    }
    return {levers.fogReach / weight,
            {levers.fogTint[0] / weight, levers.fogTint[1] / weight, levers.fogTint[2] / weight},
            levers.fogLow / weight,
            levers.fogTop / weight};
}
