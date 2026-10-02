#include "engine/vtable.h"
#include "fcse_api.h"

#include <d3d9.h>
#include <windows.h>

namespace {
    // What one IDirect3DDevice9 vtable holds: three entries from IUnknown and the interface's own.
    constexpr size_t kSlotCount = 119;

    bool g_read = false;
    void* g_slots[kSlotCount] = {};

    // A device that exists only to be asked what its vtable holds. Released by the caller.
    IDirect3DDevice9* CreateThrowaway(IDirect3D9** outD3d) {
        *outD3d = Direct3DCreate9(D3D_SDK_VERSION);
        if (*outD3d == nullptr) {
            return nullptr;
        }

        D3DPRESENT_PARAMETERS present = {};
        present.Windowed = TRUE;
        present.SwapEffect = D3DSWAPEFFECT_DISCARD;
        present.BackBufferFormat = D3DFMT_UNKNOWN;
        present.hDeviceWindow = GetDesktopWindow();

        IDirect3DDevice9* device = nullptr;
        if (FAILED((*outD3d)->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL,
                                           present.hDeviceWindow,
                                           D3DCREATE_SOFTWARE_VERTEXPROCESSING, &present,
                                           &device))) {
            (*outD3d)->Release();
            *outD3d = nullptr;
            return nullptr;
        }
        return device;
    }

    void ReadOnce() {
        if (g_read) {
            return;
        }
        g_read = true;

        IDirect3D9* d3d = nullptr;
        IDirect3DDevice9* device = CreateThrowaway(&d3d);
        if (device == nullptr) {
            return;
        }
        void** table = *reinterpret_cast<void***>(device);
        for (size_t i = 0; i < kSlotCount; i++) {
            g_slots[i] = table[i];
        }
        device->Release();
        d3d->Release();
    }
}

void* WeaponOverhaul::Vtable::Slot(size_t slot) {
    ReadOnce();
    return slot < kSlotCount ? g_slots[slot] : nullptr;
}

bool WeaponOverhaul::Vtable::Hook(size_t slot, void* detour, void** original) {
    void* target = Slot(slot);
    if (target == nullptr) {
        FCSE::ApiPointer()->Log("no Direct3D 9 device could be made to read the vtable from");
        return false;
    }
    // A rejected hook is already logged by FCSE.
    return FCSE::ApiPointer()->Hook(target, detour, original);
}
