// The engine's own Direct3D 9 device class, whose methods every world draw and every pass end go
// through. See docs/docs/engine-internals/presentation-and-input.md.
#pragma once

#include <cstddef>
#include <cstdint>
#include <d3d9.h>

namespace SkyOverhaul::EngineDevice {

// The device the game created, which under a wrapper such as ReShade or DXVK is the wrapper's.
inline IDirect3DDevice9* Direct3D(void* self) {
    constexpr size_t kDevice = 0x38;
    return *reinterpret_cast<IDirect3DDevice9**>(static_cast<uint8_t*>(self) + kDevice);
}

}
