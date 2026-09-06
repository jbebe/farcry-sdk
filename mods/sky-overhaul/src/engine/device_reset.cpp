#include "engine/device_reset.h"

#include "fcse_api.h"

#include <cstdio>

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

    // Its opposite, which re-acquires the back buffer and marks the device usable again.
    FCSE::Relocation<DeviceCallbackFn> g_restore{FCSE::Pattern(
        "A1 ?? ?? ?? ?? 83 EC 18 53 55 56 57 8B 3D ?? ?? ?? ?? 8D 1C 87 3B FB 8B E9 74 20 EB 03 "
        "8D 49 00 8B 37 8B 16 8B 42 04 8B CE FF D0")};

    DeviceCallbackFn g_originalTeardown = nullptr;
    DeviceCallbackFn g_originalRestore = nullptr;
    void (*g_onRelease)() = nullptr;

    int g_linesLogged = 0;

    void Log(const char* line) {
        if (g_linesLogged < 20) {
            g_linesLogged++;
            FCSE::ApiPointer()->Log(line);
        }
    }

    void __fastcall TeardownDetour(void* self) {
        Log("device reset: teardown - releasing what the plugin holds");
        if (g_onRelease != nullptr) {
            g_onRelease();
        }
        g_originalTeardown(self);
    }

    void __fastcall RestoreDetour(void* self) {
        g_originalRestore(self);
        Log("device reset: restored");
    }
}

bool SkyOverhaul::DeviceReset::Install(void (*onRelease)()) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_teardown || !g_restore) {
        api->Log("device reset: the renderer's device teardown was not found in this build");
        return false;
    }

    if (!api->Hook(reinterpret_cast<void*>(g_teardown.address()),
                   reinterpret_cast<void*>(&TeardownDetour),
                   reinterpret_cast<void**>(&g_originalTeardown)) ||
        !api->Hook(reinterpret_cast<void*>(g_restore.address()),
                   reinterpret_cast<void*>(&RestoreDetour),
                   reinterpret_cast<void**>(&g_originalRestore))) {
        return false;
    }

    g_onRelease = onRelease;

    char line[160];
    std::snprintf(line, sizeof(line), "device reset: teardown at 0x%08zX, restore at 0x%08zX",
                  static_cast<size_t>(g_teardown.address()),
                  static_cast<size_t>(g_restore.address()));
    api->Log(line);
    return true;
}
