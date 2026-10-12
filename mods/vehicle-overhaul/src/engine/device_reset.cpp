// Through the device's vtable rather than the renderer's teardown, which Sky Overhaul finds by a
// pattern that another plugin's hook on it would break.
#include "engine/device_reset.h"

#include "engine/vtable.h"
#include "fcse_api.h"

#include <d3d9.h>

namespace {
    constexpr size_t kResetSlot = 16;

    using ResetFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRESENT_PARAMETERS*);

    ResetFn g_originalReset = nullptr;
    void (*g_onRelease)() = nullptr;

    HRESULT __stdcall ResetDetour(IDirect3DDevice9* device, D3DPRESENT_PARAMETERS* parameters) {
        g_onRelease();
        return g_originalReset(device, parameters);
    }
}

bool VehicleOverhaul::DeviceReset::Install(void (*onRelease)()) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    void* reset = Vtable::Slot(kResetSlot);
    if (reset == nullptr) {
        api->Log("device reset: no Direct3D 9 device could be created to read the vtable from");
        return false;
    }
    g_onRelease = onRelease;
    return api->Hook(reset, reinterpret_cast<void*>(&ResetDetour), reinterpret_cast<void**>(&g_originalReset));
}
