// No sprint and aim blur: sprinting and aiming down the sights no longer blur the screen.
//
// From FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) source/graphics/postfx.ixx. One site:
// CScenePostFxBlur::Render, detoured to report nothing drawn, which keeps the source image.
#include "fcse_api.h"

#include <atomic>
#include <cstdint>

namespace {
    std::atomic<bool> g_removed{false};

    // __thiscall with four stack arguments, the callee popping them (RET 0x10).
    using RenderFn = uint8_t(__fastcall*)(void* pass, void* unused, void* frame, void* scene,
                                          void* source, void* target);

    FCSE::Relocation<uint8_t*> g_render{FCSE::Pattern(
        "55 8B EC 83 E4 F0 81 EC A4 00 00 00 8B 45 0C 53 56 57 8B D9 83 C0 08 50 B9 ?? ?? ?? ?? 89 "
        "5C 24 3C E8")};
    RenderFn g_originalRender = nullptr;

    uint8_t __fastcall RenderDetour(void* pass, void* unused, void* frame, void* scene, void* source,
                                    void* target) {
        if (g_removed.load(std::memory_order_relaxed)) {
            return 0;
        }

        return g_originalRender(pass, unused, frame, scene, source, target);
    }
}

void InstallNoBlur() {
    if (!g_render) {
        FCSE::Logf("no sprint and aim blur: the blur pass was not found in this build - the blur "
                   "cannot be removed");
        return;
    }

    if (FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(g_render.address()),
                                 reinterpret_cast<void*>(&RenderDetour),
                                 reinterpret_cast<void**>(&g_originalRender))) {
        FCSE::Logf("no sprint and aim blur: hooked the blur pass at 0x%08zX",
                   static_cast<size_t>(g_render.address()));
    }
}

void __cdecl OnNoBlurChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_removed.store(value->asCheckbox, std::memory_order_relaxed);
}
