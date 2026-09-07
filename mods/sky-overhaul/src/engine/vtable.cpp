#include "engine/vtable.h"

#include <d3d9.h>
#include <windows.h>

namespace {
    bool g_read = false;
    void* g_slots[SkyOverhaul::Vtable::kSlotCount] = {};

    void ReadOnce() {
        if (g_read) {
            return;
        }
        // Set before the attempt, not after: a machine that cannot make a device should be asked
        // once and then answer null for ever, rather than build a device per hook.
        g_read = true;

        IDirect3D9* d3d = Direct3DCreate9(D3D_SDK_VERSION);
        if (d3d == nullptr) {
            return;
        }

        D3DPRESENT_PARAMETERS present = {};
        present.Windowed = TRUE;
        present.SwapEffect = D3DSWAPEFFECT_DISCARD;
        present.BackBufferFormat = D3DFMT_UNKNOWN;
        present.hDeviceWindow = GetDesktopWindow();

        IDirect3DDevice9* device = nullptr;
        if (SUCCEEDED(d3d->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, present.hDeviceWindow,
                                        D3DCREATE_SOFTWARE_VERTEXPROCESSING, &present, &device)) &&
            device != nullptr) {
            void** table = *reinterpret_cast<void***>(device);
            for (size_t i = 0; i < SkyOverhaul::Vtable::kSlotCount; i++) {
                g_slots[i] = table[i];
            }
            device->Release();
        }
        d3d->Release();
    }
}

void* SkyOverhaul::Vtable::Slot(size_t slot) {
    ReadOnce();
    return slot < kSlotCount ? g_slots[slot] : nullptr;
}
