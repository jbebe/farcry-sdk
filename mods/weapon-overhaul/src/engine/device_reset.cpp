#include "engine/device_reset.h"

#include "engine/vtable.h"
#include "fcse_api.h"

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

bool WeaponOverhaul::DeviceReset::Install(void (*onRelease)()) {
    void* reset = Vtable::Slot(Vtable::kReset);
    if (reset == nullptr) {
        FCSE::ApiPointer()->Log("device reset: no Direct3D 9 device could be made to read from");
        return false;
    }
    g_onRelease = onRelease;
    // A rejected hook is already logged by FCSE.
    return FCSE::ApiPointer()->Hook(reset, reinterpret_cast<void*>(&ResetDetour),
                                    reinterpret_cast<void**>(&g_originalReset));
}
