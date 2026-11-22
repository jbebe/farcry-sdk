#include "engine/environment.h"

#include "engine/locate.h"
#include "engine/seqlock.h"
#include "engine/time_of_day.h"
#include "fcse_api.h"
#include "lightning.h"
#include "sky_model.h"
#include "weather/weather.h"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>

namespace {
    using SkyOverhaul::Weather::Reading;

    // CDynamicEnvironmentManager.
    constexpr size_t kFogOverride = 0x14C;
    constexpr size_t kFogOverrideWeight = 0x160;
    constexpr size_t kWindOverride = 0x1A0;
    constexpr size_t kWindOverrideWeight = 0x1B4;
    constexpr size_t kWindDirectionHeld = 0x1D0;
    constexpr size_t kWindForce = 0x3D8;
    constexpr size_t kWindDirection = 0x3DC;
    constexpr size_t kCurveStorm = 0x484;
    constexpr size_t kRainTarget = 0x48C;
    constexpr size_t kRaining = 0x49C;
    constexpr size_t kDriftDirection = 0x4B0;
    constexpr size_t kLightingOverride = 0x168;
    constexpr size_t kLightingOverrideWeight = 0x17C;
    constexpr size_t kSunHeight = 0x2BC;

    // CLightingSetup: the moon's colour, the ambient light's multiplier and its colour.
    constexpr size_t kMoonColour = 0x20;
    constexpr size_t kAmbientMultiplier = 0x30;
    constexpr size_t kAmbientColour = 0x40;

    // How much of the night's ambient full cloud takes away, and the least cloud a night is lit as:
    // C6's thick but light cumulus, so no night is brighter than its.
    constexpr float kCloudedAmbientLoss = 2.0f / 3.0f;
    constexpr float kBrightestNightCover = 0.55f;

    // A lightning flash: the most times over it raises the world's light, the dimmest ambient it is
    // weighed against, and the faintest that changes anything.
    constexpr float kMostFlash = 80.0f;
    constexpr float kDarkestAmbient = 0.03f;
    constexpr float kFaintestFlash = 0.01f;

    // The fog the manager has just blended, on its stack at the first fog setter: start, end,
    // amount, the height its strength starts from and that strength, the height it ends at and
    // that strength, then the sun side's colour and the far side's.
    constexpr size_t kFogSetting = 0x34;
    constexpr size_t kFogSunSide = 0x50;
    constexpr size_t kFogFarSide = 0x60;
    enum FogField { kStart, kEnd, kAmount, kLowHeight, kLowStrength, kHighHeight, kHighStrength };

    // Fog lying on the ground: full up to this share of the way to where it has thinned to the share
    // left aloft, and the eye's height over the feet. The game's own fog thins with height too, about
    // half at the ground, which ours does not keep.
    constexpr float kLayerFullShare = 0.15f;
    constexpr float kLeftAloft = 0.12f;
    constexpr float kEyeHeight = 1.8f;

    // Rain lighter than this is no rain, so it stops once the weather has eased it out.
    constexpr float kRainFloor = 0.02f;

    using UpdateWeatherFn = void(__fastcall*)(uint8_t* self, void* unused, float delta);
    using ApplyWindFn = void(__fastcall*)(uint8_t* self);
    using UpdateRainFn = void(__fastcall*)(uint8_t* self, void* unused, float delta, float force);
    using FinalLightingFn = void(__fastcall*)(uint8_t* self, void* unused, float timeOfDay,
                                              uint8_t* out);

    FCSE::Relocation<UpdateWeatherFn> g_updateWeatherMapped{FCSE::Uplay(0x00118FB0)};
    FCSE::Relocation<UpdateWeatherFn> g_updateWeatherScanned{FCSE::Pattern(
        "56 8B F1 80 7E 30 00 0F 85 ?? ?? ?? ?? F3 0F 10 86 5C 02 00 00 F3 0F 10 8E CC 01 00 00")};
    // By the address library alone: DevTools hooks both first, so their opening bytes are its jump
    // by the time a pattern would look.
    FCSE::Relocation<ApplyWindFn> g_applyWind{FCSE::Uplay(0x00118AE0)};
    FCSE::Relocation<UpdateRainFn> g_updateRain{FCSE::Uplay(0x0011B480)};
    FCSE::Relocation<FinalLightingFn> g_finalLightingMapped{FCSE::Uplay(0x00119100)};
    FCSE::Relocation<FinalLightingFn> g_finalLightingScanned{FCSE::Pattern(
        "55 8B EC 83 E4 F0 83 EC 78 D9 45 08 56 57 8B 7D 0C 8B F1 F3 0F 10 8E 84 04 00 00 F3 0F 10 "
        "86 B8 01 00 00")};
    // The manager's frame at its first fog setter, start loaded for the push.
    FCSE::Relocation<uint8_t*> g_fogSetters{FCSE::Pattern(
        "D9 44 24 34 51 8B 0D ?? ?? ?? ?? D9 1C 24 E8 ?? ?? ?? ?? D9 44 24 38 51 8B 0D ?? ?? ?? ?? "
        "D9 1C 24 E8")};

    UpdateWeatherFn g_originalUpdateWeather = nullptr;
    ApplyWindFn g_originalApplyWind = nullptr;
    UpdateRainFn g_originalUpdateRain = nullptr;
    FinalLightingFn g_originalFinalLighting = nullptr;

    // The manager, as the weather update last saw it.
    uint8_t* g_manager = nullptr;

    float& Float(uint8_t* base, size_t offset) {
        return *reinterpret_cast<float*>(base + offset);
    }

    float Mix(float a, float b, float t) {
        return a + (b - a) * t;
    }

    // How far a script's override of that kind has taken over, nought with none set.
    float Overridden(uint8_t* manager, size_t preset, size_t weight) {
        return *reinterpret_cast<void**>(manager + preset) != nullptr ? Float(manager, weight)
                                                                       : 0.0f;
    }

    // The storm factor every reader takes, written over the curve's; a script's storm override is
    // still blended over it when the manager reads it.
    void __fastcall UpdateWeatherDetour(uint8_t* self, void* unused, float delta) {
        g_originalUpdateWeather(self, unused, delta);
        g_manager = self;
        SkyOverhaul::Weather::Update(self);
        SkyOverhaul::Lightning::OnGameFrame();
        Reading reading;
        if (SkyOverhaul::Weather::ForEngine(reading)) {
            Float(self, kCurveStorm) = SkyOverhaul::SkyModel::StormFactor(reading.levers.storminess);
        }
    }

    void __fastcall ApplyWindDetour(uint8_t* self) {
        Reading reading;
        if (SkyOverhaul::Weather::ForEngine(reading)) {
            float& force = Float(self, kWindForce);
            force = Mix(reading.levers.wind, force,
                        Overridden(self, kWindOverride, kWindOverrideWeight));
            if (self[kWindDirectionHeld] == 0) {
                Float(self, kWindDirection) = reading.windDegrees;
                Float(self, kDriftDirection) = reading.windDegrees;
            }
        }
        g_originalApplyWind(self);
    }

    // The raining byte and the strength the rain eases to, before the manager acts on them; the
    // force tilts the rain.
    void __fastcall UpdateRainDetour(uint8_t* self, void* unused, float delta, float force) {
        Reading reading;
        if (SkyOverhaul::Weather::ForEngine(reading)) {
            const bool raining = reading.levers.rain > kRainFloor;
            self[kRaining] = raining ? 1 : 0;
            if (raining) {
                Float(self, kRainTarget) = reading.levers.rain;
            }
            force = Float(self, kWindForce);
        }
        g_originalUpdateRain(self, unused, delta, force);
    }

    // A lightning strike lights the world through its own light: the ambient raised by as much again
    // as the strike adds to what is there, and the moon's light with it at night, so moonlit
    // shadows keep a roof's shade.
    void Flash(uint8_t* out, float night) {
        const float flash = SkyOverhaul::Lightning::WorldFlash();
        if (flash < kFaintestFlash) {
            return;
        }
        const float* a = reinterpret_cast<const float*>(out + kAmbientColour);
        const float ambient =
            (0.299f * a[0] + 0.587f * a[1] + 0.114f * a[2]) * Float(out, kAmbientMultiplier);
        const float gain = (std::min)(flash / (std::max)(ambient, kDarkestAmbient), kMostFlash);
        Float(out, kAmbientMultiplier) *= 1.0f + gain;
        float* moon = reinterpret_cast<float*>(out + kMoonColour);
        for (int c = 0; c < 3; c++) {
            moon[c] *= 1.0f + gain * night;
        }
    }

    // The light the presets give for this hour, the moon's kept off the ground by the weather's
    // clouds and the night's ambient dimmed with it, never less than the brightest night's cloud
    // keeps; a script's lighting override keeps its own. A lightning flash then raises it.
    void __fastcall FinalLightingDetour(uint8_t* self, void* unused, float timeOfDay, uint8_t* out) {
        g_originalFinalLighting(self, unused, timeOfDay, out);
        const float night = SkyOverhaul::SkyModel::Night(Float(self, kSunHeight));
        Reading reading;
        if (SkyOverhaul::Weather::ForEngine(reading)) {
            const float kept = 1.0f - Overridden(self, kLightingOverride, kLightingOverrideWeight);
            const float cover =
                (std::max)(SkyOverhaul::Weather::Cover(reading.levers), kBrightestNightCover) * kept;
            float* moon = reinterpret_cast<float*>(out + kMoonColour);
            for (int c = 0; c < 3; c++) {
                moon[c] *= 1.0f - cover;
            }
            Float(out, kAmbientMultiplier) *= 1.0f - kCloudedAmbientLoss * cover * night;
        }
        Flash(out, night);
    }

    void BlendColour(float* colour, const float tint[3], float amount) {
        const float grey = colour[0] * 0.299f + colour[1] * 0.587f + colour[2] * 0.114f;
        for (int c = 0; c < 3; c++) {
            colour[c] = Mix(colour[c], tint[c] * grey, amount);
        }
    }

    // The sky's fog by height as the game's own fog has it, in the shader's form, before ours
    // reshapes the land's: the sky reads the same height pair against how far up a ray looks.
    SkyOverhaul::Seqlock<std::array<float, 4>> g_skyFogHeight;

    void PublishSkyFogHeight(const float* fog) {
        const float span = fog[kHighHeight] - fog[kLowHeight];
        const float scale = span != 0.0f ? 1.0f / span : 0.0f;
        g_skyFogHeight.Publish({scale, -fog[kLowHeight] * scale,
                                fog[kHighStrength] - fog[kLowStrength], fog[kLowStrength]});
    }

    void FogHandler(FCSE_MidHookContext* ctx) {
        float* fog = reinterpret_cast<float*>(ctx->esp + kFogSetting);
        PublishSkyFogHeight(fog);
        Reading reading;
        if (g_manager == nullptr || !SkyOverhaul::Weather::ForEngine(reading)) {
            return;
        }
        const SkyOverhaul::Weather::Fog ours = SkyOverhaul::Weather::FogOf(reading.levers);
        const float amount = reading.levers.fogWeight *
                             (1.0f - Overridden(g_manager, kFogOverride, kFogOverrideWeight));
        if (ours.end <= 0.0f || amount <= 0.0f) {
            return;
        }
        fog[kStart] = Mix(fog[kStart], 0.0f, amount);
        fog[kEnd] = Mix(fog[kEnd], ours.end, amount);
        fog[kAmount] = Mix(fog[kAmount], 1.0f, amount);

        float eye[3];
        float aloft = 1.0f;
        if (ours.low > 0.0f && SkyOverhaul::Weather::Eye(eye)) {
            const float ground = eye[2] - kEyeHeight;
            fog[kLowHeight] = ground + ours.top * kLayerFullShare;
            fog[kHighHeight] = ground + ours.top;
            aloft = Mix(1.0f, kLeftAloft, ours.low);
        }
        fog[kLowStrength] = Mix(fog[kLowStrength], 1.0f, amount);
        fog[kHighStrength] = Mix(fog[kHighStrength], aloft, amount);

        BlendColour(reinterpret_cast<float*>(ctx->esp + kFogSunSide), ours.tint, amount);
        BlendColour(reinterpret_cast<float*>(ctx->esp + kFogFarSide), ours.tint, amount);
    }

    template <typename Fn>
    bool HookSite(const char* what, uintptr_t target, Fn detour, Fn& original) {
        if (target == 0) {
            FCSE::Logf("weather: %s was not found in this build - it stays the game's", what);
            return false;
        }
        return FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(target),
                                        reinterpret_cast<void*>(detour),
                                        reinterpret_cast<void**>(&original));
    }
}

void SkyOverhaul::Environment::Install() {
    TimeOfDay::Install();
    const char* const weather = "the weather update";
    if (!HookSite(weather,
                  Locate(weather, g_updateWeatherMapped.address(), g_updateWeatherScanned.address()),
                  &UpdateWeatherDetour, g_originalUpdateWeather)) {
        return;
    }
    HookSite("the wind push", g_applyWind.address(), &ApplyWindDetour, g_originalApplyWind);
    HookSite("the rain update", g_updateRain.address(), &UpdateRainDetour, g_originalUpdateRain);
    const char* const light = "the night light";
    HookSite(light,
             Locate(light, g_finalLightingMapped.address(), g_finalLightingScanned.address()),
             &FinalLightingDetour, g_originalFinalLighting);
    if (!g_fogSetters || !FCSE::ApiPointer()->MidHook(g_fogSetters.get(), &FogHandler)) {
        FCSE::Logf("weather: the fog was not found in this build - it stays the game's");
    }
}

bool SkyOverhaul::Environment::SkyFogHeight(float out[4]) {
    std::array<float, 4> values;
    if (!g_skyFogHeight.Latest(values)) {
        return false;
    }
    std::copy(values.begin(), values.end(), out);
    return true;
}
