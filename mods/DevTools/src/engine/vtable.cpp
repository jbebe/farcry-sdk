#include "engine/vtable.h"

#include <d3d9.h>
#include <windows.h>

#include <algorithm>
#include <array>

namespace {
    // IDirect3DDevice9's three IUnknown and 116 own methods.
    constexpr size_t kSlots = 119;

    // Builds a device only to copy its vtable. The device is gone before this returns.
    std::array<void*, kSlots> Copy() {
        std::array<void*, kSlots> slots{};
        IDirect3D9* d3d = Direct3DCreate9(D3D_SDK_VERSION);
        if (d3d == nullptr) {
            return slots;
        }

        D3DPRESENT_PARAMETERS present{};
        present.Windowed = TRUE;
        present.SwapEffect = D3DSWAPEFFECT_DISCARD;
        present.BackBufferFormat = D3DFMT_UNKNOWN;
        present.hDeviceWindow = GetDesktopWindow();

        IDirect3DDevice9* device = nullptr;
        if (SUCCEEDED(d3d->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, present.hDeviceWindow,
                                        D3DCREATE_SOFTWARE_VERTEXPROCESSING, &present, &device))) {
            std::copy_n(*reinterpret_cast<void***>(device), kSlots, slots.begin());
            device->Release();
        }
        d3d->Release();
        return slots;
    }
}

namespace DevTools::Vtable {

void* Slot(size_t slot) {
    static const std::array<void*, kSlots> slots = Copy();
    return slot < kSlots ? slots[slot] : nullptr;
}

}
