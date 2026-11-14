#include "engine/device_reset.h"

#include "engine/memory.h"

namespace {
    // The engine device's own Reset, __thiscall taking only `this`, declared __fastcall because
    // MSVC will not let a free function be __thiscall. True when Direct3D's reset succeeded.
    using ResetFn = bool(__fastcall*)(void* self);
    FCSE::Relocation<ResetFn> g_reset{FCSE::Uplay(0x00422630)};

    ResetFn g_originalReset = nullptr;
    void (*g_onRelease)() = nullptr;

    bool __fastcall ResetDetour(void* self) {
        g_onRelease();
        return g_originalReset(self);
    }
}

bool AimingOverhaul::DeviceReset::Install(void (*onRelease)()) {
    g_onRelease = onRelease;
    return Hook(g_reset.address(), &ResetDetour, &g_originalReset);
}
