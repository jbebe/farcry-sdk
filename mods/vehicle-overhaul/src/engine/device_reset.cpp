#include "engine/device_reset.h"

#include "fcse_api.h"

namespace {
    // The engine device's Reset, __thiscall declared __fastcall; true when Direct3D's reset succeeded.
    using ResetFn = bool(__fastcall*)(void* self);
    FCSE::Relocation<ResetFn> g_reset{FCSE::Uplay(0x00422630)};

    ResetFn g_originalReset = nullptr;
    void (*g_onRelease)() = nullptr;

    bool __fastcall ResetDetour(void* self) {
        g_onRelease();
        return g_originalReset(self);
    }
}

bool VehicleOverhaul::DeviceReset::Install(void (*onRelease)()) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (!g_reset) {
        api->Log("device reset: the engine device's Reset was not found in this build");
        return false;
    }
    g_onRelease = onRelease;
    return api->Hook(reinterpret_cast<void*>(g_reset.address()), reinterpret_cast<void*>(&ResetDetour),
                     reinterpret_cast<void**>(&g_originalReset));
}
