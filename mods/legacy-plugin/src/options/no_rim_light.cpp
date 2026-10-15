// No rim lighting: the glow outlining characters, mainly visible at night, is gone.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/graphics/rimlighting.ixx, which
// credits Boggalog's Far Cry 2 Patched. One site: the per-frame shader parameter fill, detoured so
// the rim light intensity it writes last is zeroed after it.
#include "fcse_api.h"

#include <atomic>
#include <cstdint>

namespace {
    // On the render frame: its CGlobalShaderParameterProvider, and on that the rim light intensity.
    constexpr uintptr_t kFrameParameterProvider = 0x60;
    constexpr uintptr_t kProviderRimLightIntensity = 0xD0;

    std::atomic<bool> g_removed{false};

    using FillFn = void(__fastcall*)(void* frame);

    FCSE::Relocation<uint8_t*> g_fill{FCSE::Pattern(
        "55 8B EC 83 E4 F0 83 EC 24 53 56 57 8B F1 E8 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? 8B F8 89 7C 24 18 "
        "E8 ?? ?? ?? ?? F3 0F 10 57 10")};
    FillFn g_originalFill = nullptr;

    // After the original, so the dirty bits it set still upload the value.
    void __fastcall FillDetour(void* frame) {
        g_originalFill(frame);

        if (!g_removed.load(std::memory_order_relaxed) || frame == nullptr) {
            return;
        }

        auto* provider =
            *reinterpret_cast<uint8_t**>(static_cast<uint8_t*>(frame) + kFrameParameterProvider);
        if (provider == nullptr) {
            return;
        }

        *reinterpret_cast<float*>(provider + kProviderRimLightIntensity) = 0.0f;
    }
}

void InstallNoRimLight() {
    if (!g_fill) {
        FCSE::Logf("no rim lighting: the shader parameter fill was not found in this build - rim "
                   "lighting cannot be removed");
        return;
    }

    if (FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(g_fill.address()),
                                 reinterpret_cast<void*>(&FillDetour),
                                 reinterpret_cast<void**>(&g_originalFill))) {
        FCSE::Logf("no rim lighting: hooked the shader parameter fill at 0x%08zX",
                   static_cast<size_t>(g_fill.address()));
    }
}

void __cdecl OnNoRimLightChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_removed.store(value->asCheckbox, std::memory_order_relaxed);
}
