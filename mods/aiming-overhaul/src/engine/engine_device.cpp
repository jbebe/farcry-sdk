#include "engine/engine_device.h"

#include "engine/memory.h"

#include <cstddef>
#include <cstdint>

namespace {
    constexpr ptrdiff_t kDevice = 0x38;

    using SendConstantsFn = void(__cdecl*)(IDirect3DDevice9* device);
    FCSE::Relocation<SendConstantsFn> g_sendConstants{FCSE::Uplay(0x00422EB0)};
}

IDirect3DDevice9* AimingOverhaul::EngineDevice::Direct3D(void* self) {
    return Field<IDirect3DDevice9*>(static_cast<uint8_t*>(self), kDevice);
}

bool AimingOverhaul::EngineDevice::CanSendConstants() {
    return g_sendConstants.address() != 0;
}

void AimingOverhaul::EngineDevice::SendConstants(IDirect3DDevice9* device) {
    g_sendConstants.get()(device);
}
