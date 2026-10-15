// Brown filter: on is the game's own grade, off a neutral one without the brown-orange remap.
//
// The neutral values (remap and contrast 0, saturation 1) are Scubrah's Patch's,
// legacy/scubrahs-patch/features/original-colorgrading.md; the hook on the adaptive bloom pass's
// settings is FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/graphics/saturation.ixx,
// extended from saturation alone to the remap and contrast. One site: the pass's entry, which is
// handed the frame's CSceneAdaptiveBloom.
#include "fcse_api.h"

#include <atomic>
#include <cstdint>
#include <initializer_list>

namespace {
    // CSceneAdaptiveBloom, rewritten from the weather presets every frame before the pass reads it.
    constexpr uintptr_t kColorRemapRed = 0x2C;
    constexpr uintptr_t kColorRemapGreen = 0x30;
    constexpr uintptr_t kColorRemapBlue = 0x34;
    constexpr uintptr_t kContrast = 0x38;
    constexpr uintptr_t kSaturation = 0x3C;

    std::atomic<bool> g_neutral{false};

    FCSE::Relocation<uint8_t*> g_bloomPass{FCSE::Pattern(
        "55 8B EC 83 E4 F0 81 EC 44 01 00 00 53 56 57 8B F1 E8 ?? ?? ?? ?? 8B 8E 90 00 00 00 6A 00 "
        "68 ?? ?? ?? ??")};

    // The settings are the first stack argument.
    void BloomPassHandler(FCSE_MidHookContext* ctx) {
        if (!g_neutral.load(std::memory_order_relaxed)) {
            return;
        }

        auto* settings = *reinterpret_cast<uint8_t**>(ctx->esp + 4);
        if (settings == nullptr) {
            return;
        }

        for (uintptr_t field : {kColorRemapRed, kColorRemapGreen, kColorRemapBlue, kContrast}) {
            *reinterpret_cast<float*>(settings + field) = 0.0f;
        }
        *reinterpret_cast<float*>(settings + kSaturation) = 1.0f;
    }
}

void InstallBrownFilter() {
    if (!g_bloomPass) {
        FCSE::Logf("brown filter: the adaptive bloom pass was not found in this build - the grade "
                   "cannot be changed");
        return;
    }

    if (FCSE::ApiPointer()->MidHook(reinterpret_cast<void*>(g_bloomPass.address()),
                                    &BloomPassHandler)) {
        FCSE::Logf("brown filter: hooked the adaptive bloom pass at 0x%08zX",
                   static_cast<size_t>(g_bloomPass.address()));
    }
}

void __cdecl OnBrownFilterChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_neutral.store(!value->asCheckbox, std::memory_order_relaxed);
}
