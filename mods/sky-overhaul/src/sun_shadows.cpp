#include "sun_shadows.h"

#include "fcse_api.h"
#include "tuning.h"

#include <cstdint>

namespace {
    // The code loading gfx_SunShadow_InertiaStartAngle and gfx_SunShadow_InertiaEndAngle, in
    // degrees. See docs/docs/engine-internals/time-of-day-and-lighting.md.
    FCSE::Relocation<uint8_t*> g_angleLoads{FCSE::Pattern(
        "F3 0F 10 0D ?? ?? ?? ?? F3 0F 10 1D ?? ?? ?? ?? F3 0F 10 2D ?? ?? ?? ?? "
        "F3 0F 59 CA F3 0F 59 DA")};
    constexpr size_t kStartOperand = 4;
    constexpr size_t kEndOperand = 12;

    // The renderer allocated, 0x3B8 bytes, and its pointer stored. Its own copy of CRenderConfig
    // holds gfx_SunShadowRange0 to 2, the three cascades' reach in metres.
    FCSE::Relocation<uint8_t*> g_rendererStore{FCSE::Pattern(
        "6A 00 68 B8 03 00 00 E8 ?? ?? ?? ?? 83 C4 08 85 C0 74 0D 8B C8 "
        "E8 ?? ?? ?? ?? A3 ?? ?? ?? ?? C3")};
    constexpr size_t kRendererOperand = 27;
    constexpr size_t kRenderConfigOffset = 0x2C;
    constexpr size_t kRangesOffset = 0x778 + 0x40;

    // One of the engine's settings, held at our value while the part is on and given back after.
    struct Held {
        float ours;
        float engine;
        bool written;
    };

    // The start and end angles, then the three cascades' ranges.
    constexpr size_t kSettings = 5;

    bool g_enabled = false;
    bool g_logged = false;
    Held g_held[kSettings] = {};

    // The global an instruction found by `code` addresses, `at` bytes into it.
    template <class T>
    T* Operand(const FCSE::Relocation<uint8_t*>& code, size_t at) {
        return code ? *reinterpret_cast<T**>(code.get() + at) : nullptr;
    }

    // Where the engine keeps setting `i`, in kSettings order, or null where this build has none.
    float* Setting(size_t i) {
        if (i < 2) {
            return Operand<float>(g_angleLoads, i == 0 ? kStartOperand : kEndOperand);
        }
        uint8_t** renderer = Operand<uint8_t*>(g_rendererStore, kRendererOperand);
        if (renderer == nullptr || *renderer == nullptr) {
            return nullptr;
        }
        uint8_t* config = *reinterpret_cast<uint8_t**>(*renderer + kRenderConfigOffset);
        return config == nullptr ? nullptr
                                 : reinterpret_cast<float*>(config + kRangesOffset) + (i - 2);
    }

    // Anything but what we wrote last is the engine's own value, set since.
    void Hold(float* setting, Held& held, float value) {
        if (setting == nullptr) {
            return;
        }
        if (!held.written || *setting != held.ours) {
            held.engine = *setting;
        }
        held.ours = value;
        *setting = value;
        held.written = true;
    }

    void Release(float* setting, Held& held) {
        if (setting != nullptr && held.written) {
            *setting = held.engine;
        }
        held.written = false;
    }
}

void SkyOverhaul::SunShadows::SetEnabled(bool enabled) {
    g_enabled = enabled;
    if (enabled) {
        return;
    }
    for (size_t i = 0; i < kSettings; i++) {
        Release(Setting(i), g_held[i]);
    }
}

void SkyOverhaul::SunShadows::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky) {
        return;
    }
    const Tuning::Values v = Tuning::Current();
    const float ours[kSettings] = {v.sunShadowStartAngle, v.sunShadowEndAngle,
                                   v.sunShadowRange0, v.sunShadowRange1, v.sunShadowRange2};
    for (size_t i = 0; i < kSettings; i++) {
        Hold(Setting(i), g_held[i], ours[i]);
    }
    if (!g_logged) {
        g_logged = true;
        FCSE::Logf("sun shadows: angles %s (engine %.0f and %.0f), ranges %s (engine %.0f, %.0f "
                   "and %.0f m)",
                   g_held[0].written ? "held" : "not found in this build", g_held[0].engine,
                   g_held[1].engine, g_held[2].written ? "held" : "not found in this build",
                   g_held[2].engine, g_held[3].engine, g_held[4].engine);
    }
}
