// The environment manager's rain and wind updates.
//
// What each field is and when the engine writes it is in
// docs/docs/engine-internals/time-of-day-and-lighting.md.
#include "engine/weather.h"

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>
#include <mutex>

namespace {
    using DevTools::Weather::Mode;

    // CDynamicEnvironmentManager.
    constexpr ptrdiff_t kOverrideValue = 0x1B8;
    constexpr ptrdiff_t kOverrideTarget = 0x1C0;
    constexpr ptrdiff_t kOverrideDuration = 0x1C4;
    constexpr ptrdiff_t kOverrideWeight = 0x1CC;
    constexpr ptrdiff_t kDesert = 0x258;
    constexpr ptrdiff_t kTimeScale = 0x25C;
    constexpr ptrdiff_t kWindForce = 0x3D8;
    constexpr ptrdiff_t kWindDirection = 0x3DC;
    constexpr ptrdiff_t kStormHour = 0x480;
    constexpr ptrdiff_t kCurveStorm = 0x484;
    constexpr ptrdiff_t kRainTarget = 0x48C;
    constexpr ptrdiff_t kRainIntensity = 0x498;
    constexpr ptrdiff_t kRaining = 0x49C;
    constexpr ptrdiff_t kRollTimer = 0x4A0;
    constexpr ptrdiff_t kRollThreshold = 0x4A4;
    constexpr ptrdiff_t kDriftDirection = 0x4B0;

    using UpdateRainFn = void(__fastcall*)(void* self, void* unused, float delta, float windForce);
    using EvaluateRainFn = void(__fastcall*)(void* self, void* unused, float delta, float cover);
    using ApplyWindFn = void(__fastcall*)(void* self);

    FCSE::Relocation<UpdateRainFn> g_updateRain{FCSE::Uplay(0x0011B480)};
    FCSE::Relocation<EvaluateRainFn> g_evaluateRain{FCSE::Uplay(0x00118D00)};
    FCSE::Relocation<ApplyWindFn> g_applyWind{FCSE::Uplay(0x00118AE0)};

    UpdateRainFn g_originalUpdateRain = nullptr;
    EvaluateRainFn g_originalEvaluateRain = nullptr;
    ApplyWindFn g_originalApplyWind = nullptr;

    std::mutex g_lock;
    DevTools::Weather::Wanted g_wanted;
    DevTools::Weather::Snapshot g_snapshot;

    // The engine thread's own: what this frame applies, whether last frame forced the storm, and the
    // cover this frame's roll saw.
    DevTools::Weather::Wanted g_applied;
    bool g_stormForced = false;
    float g_cloudCover = 0.0f;

    float& Float(void* manager, ptrdiff_t offset) {
        return *reinterpret_cast<float*>(static_cast<uint8_t*>(manager) + offset);
    }

    uint8_t& Byte(void* manager, ptrdiff_t offset) {
        return *(static_cast<uint8_t*>(manager) + offset);
    }

    float ForcedWindForce() { return g_applied.wind == Mode::Force ? g_applied.windForce : 0.0f; }

    // The scripted storm override, rewritten every frame because the environment reset clears it.
    void ApplyStorm(void* manager) {
        const bool forced = g_applied.storm != Mode::Engine;
        if (forced) {
            Float(manager, kOverrideValue) =
                g_applied.storm == Mode::Force ? g_applied.stormStrength : 0.0f;
            Float(manager, kOverrideTarget) = 1.0f;
            Float(manager, kOverrideDuration) = 0.0f;
        } else if (g_stormForced) {
            Float(manager, kOverrideTarget) = 0.0f;
            Float(manager, kOverrideDuration) = 0.0f;
        }
        g_stormForced = forced;
    }

    DevTools::Weather::Snapshot Capture(void* manager) {
        DevTools::Weather::Snapshot snapshot;
        snapshot.live = true;

        snapshot.curveStorm = Float(manager, kCurveStorm);
        snapshot.stormHour = Float(manager, kStormHour);
        snapshot.desert = Float(manager, kDesert);
        snapshot.overrideValue = Float(manager, kOverrideValue);
        snapshot.overrideWeight = Float(manager, kOverrideWeight);
        snapshot.storm = snapshot.curveStorm +
                         (snapshot.overrideValue - snapshot.curveStorm) * snapshot.overrideWeight;

        snapshot.raining = Byte(manager, kRaining) != 0;
        snapshot.rainUnseen = snapshot.raining && Float(manager, kRainTarget) == 0.0f;
        snapshot.rainIntensity = Float(manager, kRainIntensity);
        snapshot.cloudCover = g_cloudCover;
        snapshot.rollThreshold = Float(manager, kRollThreshold);
        const float timeScale = Float(manager, kTimeScale);
        if (g_cloudCover > snapshot.rollThreshold && timeScale > 0.0f) {
            snapshot.nextRoll = Float(manager, kRollTimer) / timeScale;
        }

        snapshot.windForce = Float(manager, kWindForce);
        snapshot.windDegrees = Float(manager, kWindDirection);
        return snapshot;
    }

    void __fastcall EvaluateRainDetour(void* self, void* unused, float delta, float cover) {
        g_cloudCover = cover;
        g_originalEvaluateRain(self, unused, delta, cover);
    }

    void __fastcall ApplyWindDetour(void* self) {
        if (g_applied.wind != Mode::Engine) {
            Float(self, kWindForce) = ForcedWindForce();
            Float(self, kWindDirection) = g_applied.windDegrees;
        }
        g_originalApplyWind(self);
    }

    // The last call of the manager's frame. Forces the raining byte and drift direction for this call
    // only.
    void __fastcall UpdateRainDetour(void* self, void* unused, float delta, float windForce) {
        ApplyStorm(self);

        uint8_t& raining = Byte(self, kRaining);
        float& drift = Float(self, kDriftDirection);
        const uint8_t engineRaining = raining;
        const float engineDrift = drift;

        if (g_applied.rain != Mode::Engine) {
            raining = g_applied.rain == Mode::Force ? 1 : 0;
        }
        if (g_applied.wind != Mode::Engine) {
            drift = g_applied.windDegrees;
            windForce = ForcedWindForce();
        }

        g_originalUpdateRain(self, unused, delta, windForce);

        const DevTools::Weather::Snapshot snapshot = Capture(self);
        raining = engineRaining;
        drift = engineDrift;

        std::lock_guard<std::mutex> held(g_lock);
        g_snapshot = snapshot;
        g_applied = g_wanted;
    }

    template <typename Fn>
    void HookSite(FCSE::Relocation<Fn>& site, Fn detour, Fn& original, const char* name) {
        if (!site) {
            FCSE::Logf("weather: %s was not found in this build - it stays the engine's", name);
            return;
        }

        if (FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site.address()),
                                     reinterpret_cast<void*>(detour),
                                     reinterpret_cast<void**>(&original))) {
            FCSE::Logf("weather: hooked %s at 0x%08zX", name, static_cast<size_t>(site.address()));
        }
    }
}

namespace DevTools::Weather {

void Install() {
    HookSite(g_evaluateRain, &EvaluateRainDetour, g_originalEvaluateRain, "the rain roll");
    HookSite(g_applyWind, &ApplyWindDetour, g_originalApplyWind, "the wind push");
    HookSite(g_updateRain, &UpdateRainDetour, g_originalUpdateRain, "the rain update");
}

void Set(const Wanted& wanted) {
    std::lock_guard<std::mutex> held(g_lock);
    g_wanted = wanted;
}

Snapshot Read() {
    std::lock_guard<std::mutex> held(g_lock);
    return g_snapshot;
}

}
