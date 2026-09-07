#include "engine/vtable.h"

#include <d3d9.h>
#include <windows.h>

namespace {
    bool g_read = false;
    void* g_slots[SkyOverhaul::Vtable::kSlotCount] = {};

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
        // Set before the attempt, not after: a machine that cannot make a device should be asked
        // once and then answer null for ever, rather than build a device per hook.
        g_read = true;

        IDirect3D9* d3d = nullptr;
        IDirect3DDevice9* device = CreateThrowaway(&d3d);
        if (device == nullptr) {
            return;
        }
        void** table = *reinterpret_cast<void***>(device);
        for (size_t i = 0; i < SkyOverhaul::Vtable::kSlotCount; i++) {
            g_slots[i] = table[i];
        }
        device->Release();
        d3d->Release();
    }
}

void* SkyOverhaul::Vtable::Slot(size_t slot) {
    ReadOnce();
    return slot < kSlotCount ? g_slots[slot] : nullptr;
}

bool SkyOverhaul::Vtable::IsConstantSetter(size_t slot, bool pixel) {
    void* candidate = Slot(slot);
    if (candidate == nullptr) {
        return false;
    }

    IDirect3D9* d3d = nullptr;
    IDirect3DDevice9* device = CreateThrowaway(&d3d);
    if (device == nullptr) {
        return false;
    }

    using SetConstantFn = HRESULT(__stdcall*)(IDirect3DDevice9*, UINT, const float*, UINT);

    // Four values nothing else would leave behind, in a register high enough that none of what the
    // runtime seeds is disturbed. The buffer handed over is larger than the one register it
    // describes and holds no answer of its own, so that a neighbouring slot - the matching getter,
    // or one of the integer setters - writes into slack rather than over the expectation it is
    // about to be compared against.
    constexpr UINT kProbeRegister = 200;
    const float expected[4] = {0.125f, -3.5f, 17.25f, 1234.5f};
    float probe[16] = {expected[0], expected[1], expected[2], expected[3]};
    float read[4] = {};

    reinterpret_cast<SetConstantFn>(candidate)(device, kProbeRegister, probe, 1);
    const HRESULT got = pixel
                            ? device->GetPixelShaderConstantF(kProbeRegister, read, 1)
                            : device->GetVertexShaderConstantF(kProbeRegister, read, 1);

    device->Release();
    d3d->Release();

    if (FAILED(got)) {
        return false;
    }
    for (size_t i = 0; i < 4; i++) {
        if (read[i] != expected[i]) {
            return false;
        }
    }
    return true;
}
