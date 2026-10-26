// Every IDirect3DDevice9 in a process shares one vtable, so a throwaway device of our own names the
// methods the game's device will call.
#include "engine/direct3d.h"

#include "fcse_api.h"

#include <d3d9.h>
#include <windows.h>

namespace InventoryOverhaul::Direct3D {

namespace {
    // Three entries from IUnknown and the interface's own.
    constexpr size_t kSlotCount = 119;

    void* g_slots[kSlotCount] = {};

    bool ReadSlots() {
        IDirect3D9* d3d = Direct3DCreate9(D3D_SDK_VERSION);
        if (d3d == nullptr) {
            return false;
        }
        D3DPRESENT_PARAMETERS present = {};
        present.Windowed = TRUE;
        present.SwapEffect = D3DSWAPEFFECT_DISCARD;
        present.BackBufferFormat = D3DFMT_UNKNOWN;
        present.hDeviceWindow = GetDesktopWindow();
        IDirect3DDevice9* device = nullptr;
        if (FAILED(d3d->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, present.hDeviceWindow,
                                     D3DCREATE_SOFTWARE_VERTEXPROCESSING, &present, &device))) {
            d3d->Release();
            return false;
        }
        void** table = *reinterpret_cast<void***>(device);
        for (size_t i = 0; i < kSlotCount; ++i) {
            g_slots[i] = table[i];
        }
        device->Release();
        d3d->Release();
        return true;
    }
}

bool Hook(size_t slot, void* detour, void** original) {
    static const bool read = ReadSlots();
    if (!read || slot >= kSlotCount) {
        FCSE::ApiPointer()->Log("no Direct3D 9 device could be made to read the vtable from");
        return false;
    }
    return FCSE::ApiPointer()->Hook(g_slots[slot], detour, original);
}

}
