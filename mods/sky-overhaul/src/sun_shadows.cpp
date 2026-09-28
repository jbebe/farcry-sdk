#include "sun_shadows.h"

#include "fcse_api.h"
#include "tuning.h"

#include <algorithm>
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

    // CSky::UpdateShadow, which aims the shadows every frame. Its prologue asks a manager for the
    // scene state it writes to: the manager's address and that call's displacement are read from it.
    FCSE::Relocation<uint8_t*> g_updateShadow{FCSE::Pattern(
        "55 8B EC 83 E4 F0 81 EC F4 00 00 00 53 56 57 8B F9 6A 01 57 B9 ?? ?? ?? ?? "
        "E8 ?? ?? ?? ?? 8A 5D 08 84 DB")};
    constexpr size_t kManagerOperand = 21;
    constexpr size_t kCallOperand = 26;
    constexpr size_t kShadowDirection = 0x328;

    // How far the sun turns before the shadows follow it: half a degree, as a cosine. The cascades
    // snap to a texel grid that the turning sun slides, so thin shadows crawl unless it holds still.
    constexpr float kStepCosine = 0.99996192f;

    // __fastcall stands in for __thiscall, which MSVC will not let a free function be.
    using UpdateShadowFn = void(__fastcall*)(void* sky, void* unused, uint32_t isSunLight);
    using WritableStateFn = uint8_t*(__fastcall*)(void* manager, void* unused, void* sky,
                                                   uint32_t writable);

    UpdateShadowFn g_originalUpdateShadow = nullptr;
    WritableStateFn g_writableState = nullptr;
    void* g_manager = nullptr;

    // The direction the shadows are held at, once there is one.
    float g_aim[3] = {};
    bool g_holding = false;

    // One of the engine's settings, held at our value while the part is on and given back after.
    struct Held {
        float ours;
        float engine;
        bool written;
    };

    // The start and end angles, then the three cascades' ranges.
    constexpr size_t kSettings = 5;

    bool g_enabled = false;
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

    // Asking for the state again in the same frame returns the copy the original just wrote.
    void __fastcall UpdateShadowDetour(void* sky, void* unused, uint32_t isSunLight) {
        g_originalUpdateShadow(sky, unused, isSunLight);
        if (!g_enabled) {
            g_holding = false;
            return;
        }
        float* aimed =
            reinterpret_cast<float*>(g_writableState(g_manager, nullptr, sky, 1) + kShadowDirection);
        const float along = aimed[0] * g_aim[0] + aimed[1] * g_aim[1] + aimed[2] * g_aim[2];
        if (!g_holding || along < kStepCosine) {
            std::copy(aimed, aimed + 3, g_aim);
            g_holding = true;
        }
        std::copy(g_aim, g_aim + 3, aimed);
    }
}

bool SkyOverhaul::SunShadows::Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    uint8_t* code = g_updateShadow ? g_updateShadow.get() : nullptr;
    if (code == nullptr) {
        api->Log("sun shadows: the shadow aim was not found in this build, shadows turn every frame");
        return false;
    }
    g_manager = *reinterpret_cast<void**>(code + kManagerOperand);
    const int32_t displacement = *reinterpret_cast<int32_t*>(code + kCallOperand);
    g_writableState =
        reinterpret_cast<WritableStateFn>(code + kCallOperand + sizeof(int32_t) + displacement);

    if (!api->Hook(code, reinterpret_cast<void*>(&UpdateShadowDetour),
                   reinterpret_cast<void**>(&g_originalUpdateShadow))) {
        api->Log("sun shadows: the shadow aim could not be hooked, shadows turn every frame");
        return false;
    }
    return true;
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
}

