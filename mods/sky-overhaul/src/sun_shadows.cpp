#include "sun_shadows.h"

#include "fcse_api.h"

#include <cstdint>

namespace {
    // The shadow's direction loading gfx_SunShadow_InertiaStartAngle and
    // gfx_SunShadow_InertiaEndAngle, in degrees, before scaling both to radians: below the first it
    // stops following the sun and eases toward the second. The globals are found through the code
    // that reads them, since the address library maps no data. See
    // docs/docs/engine-internals/time-of-day-and-lighting.md.
    FCSE::Relocation<uint8_t*> g_angleLoads{FCSE::Pattern(
        "F3 0F 10 0D ?? ?? ?? ?? F3 0F 10 1D ?? ?? ?? ?? F3 0F 10 2D ?? ?? ?? ?? "
        "F3 0F 59 CA F3 0F 59 DA")};
    constexpr size_t kStartOperand = 4;
    constexpr size_t kEndOperand = 12;

    // The renderer allocated, 0x3B8 bytes, and its pointer stored. It draws from a copy of
    // CRenderConfig of its own, whose shadow section holds gfx_SunShadowRange0 to 2, the three
    // cascades' reach in metres; the global CRenderConfig is only what that copy was made from.
    FCSE::Relocation<uint8_t*> g_rendererStore{FCSE::Pattern(
        "6A 00 68 B8 03 00 00 E8 ?? ?? ?? ?? 83 C4 08 85 C0 74 0D 8B C8 "
        "E8 ?? ?? ?? ?? A3 ?? ?? ?? ?? C3")};
    constexpr size_t kRendererOperand = 27;
    constexpr size_t kRenderConfigOffset = 0x2C;
    constexpr size_t kRangesOffset = 0x778 + 0x40;

    // Shadows follow the sun down to fifteen degrees and lie no flatter than about ten at sunset,
    // where the engine's fifty and twenty-five hold them at mid-morning all afternoon.
    constexpr float kStart = 15.0f;
    constexpr float kEnd = 8.0f;

    // Each cascade's reach, where the engine has 4, 20 and 140. Past about eighteen metres for the
    // first, whole stretches of ground lose their shadow looking away from a low sun.
    constexpr float kRanges[3] = {18.0f, 100.0f, 500.0f};

    // One of the engine's settings, held at our value while the part is on and given back after.
    struct Held {
        float ours;
        float engine;
        bool written;
    };

    bool g_enabled = false;
    Held g_start = {kStart};
    Held g_end = {kEnd};
    Held g_ranges[3] = {{kRanges[0]}, {kRanges[1]}, {kRanges[2]}};

    // The global an instruction found by `code` addresses, `at` bytes into it.
    template <class T>
    T* Operand(const FCSE::Relocation<uint8_t*>& code, size_t at) {
        return code ? *reinterpret_cast<T**>(code.get() + at) : nullptr;
    }

    float* StartAngle() {
        return Operand<float>(g_angleLoads, kStartOperand);
    }

    float* EndAngle() {
        return Operand<float>(g_angleLoads, kEndOperand);
    }

    float* Range(size_t cascade) {
        uint8_t** renderer = Operand<uint8_t*>(g_rendererStore, kRendererOperand);
        if (renderer == nullptr || *renderer == nullptr) {
            return nullptr;
        }
        uint8_t* config = *reinterpret_cast<uint8_t**>(*renderer + kRenderConfigOffset);
        return config == nullptr ? nullptr
                                 : reinterpret_cast<float*>(config + kRangesOffset) + cascade;
    }

    void Hold(float* setting, Held& held) {
        if (setting == nullptr) {
            return;
        }
        if (*setting != held.ours) {
            held.engine = *setting;
            *setting = held.ours;
        }
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
    Release(StartAngle(), g_start);
    Release(EndAngle(), g_end);
    for (size_t cascade = 0; cascade < 3; cascade++) {
        Release(Range(cascade), g_ranges[cascade]);
    }
}

void SkyOverhaul::SunShadows::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky) {
        return;
    }
    const bool first = !g_start.written;
    Hold(StartAngle(), g_start);
    Hold(EndAngle(), g_end);
    for (size_t cascade = 0; cascade < 3; cascade++) {
        Hold(Range(cascade), g_ranges[cascade]);
    }
    if (first) {
        FCSE::Logf("sun shadows: angles %s (engine %.0f and %.0f), ranges %s (engine %.0f, %.0f "
                   "and %.0f m)",
                   g_start.written ? "held" : "not found in this build", g_start.engine,
                   g_end.engine, g_ranges[0].written ? "held" : "not found in this build",
                   g_ranges[0].engine, g_ranges[1].engine, g_ranges[2].engine);
    }
}
