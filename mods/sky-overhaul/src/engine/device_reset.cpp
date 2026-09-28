#include "engine/device_reset.h"

#include "fcse_api.h"

namespace {
    // __thiscall taking only `this`, declared __fastcall because MSVC will not let a free function
    // be __thiscall.
    using DeviceCallbackFn = void(__fastcall*)(void* self);

    // The renderer's device teardown: it clears the render target, releases what it holds and runs
    // the engine's own device-lost listeners. The two absolute addresses in the prologue are
    // wildcards because they differ between builds.
    FCSE::Relocation<DeviceCallbackFn> g_teardown{FCSE::Pattern(
        "A1 ?? ?? ?? ?? 83 EC 18 53 55 56 8B 35 ?? ?? ?? ?? 8D 04 80 57 8D 3C 86 3B F7 8B D9 "
        "C6 05 ?? ?? ?? ?? 00 74 10 8B 16 8B 42 04 8B CE FF D0")};

    DeviceCallbackFn g_originalTeardown = nullptr;
    void (*g_onRelease)() = nullptr;

    void __fastcall TeardownDetour(void* self) {
        if (g_onRelease != nullptr) {
            g_onRelease();
        }
        g_originalTeardown(self);
    }
}

bool SkyOverhaul::DeviceReset::Install(void (*onRelease)()) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_teardown) {
        api->Log("device reset: the renderer's device teardown was not found in this build");
        return false;
    }

    if (!api->Hook(reinterpret_cast<void*>(g_teardown.address()),
                   reinterpret_cast<void*>(&TeardownDetour),
                   reinterpret_cast<void**>(&g_originalTeardown))) {
        return false;
    }

    g_onRelease = onRelease;
    return true;
}
