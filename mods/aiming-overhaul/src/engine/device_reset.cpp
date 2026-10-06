#include "engine/device_reset.h"

#include "engine/vtable.h"

#include <d3d9.h>

namespace {
    using ResetFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRESENT_PARAMETERS*);

    ResetFn g_originalReset = nullptr;
    void (*g_onRelease)() = nullptr;

    HRESULT __stdcall ResetDetour(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* parameters) {
        g_onRelease();
        return g_originalReset(device, parameters);
    }
}

bool AimingOverhaul::DeviceReset::Install(void (*onRelease)()) {
    g_onRelease = onRelease;
    return Vtable::Hook(Vtable::kReset, &ResetDetour, &g_originalReset);
}
