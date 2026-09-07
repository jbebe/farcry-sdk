// Handing a compiled shader to the device.
#pragma once

#include <cstring>
#include <d3d9.h>

namespace SkyOverhaul {

// fxc writes the blob as bytes and the device wants whole tokens, so it is copied into an aligned
// buffer on the way through.
template <size_t N>
HRESULT CreateShader(IDirect3DDevice9* device, const BYTE (&blob)[N],
                     IDirect3DPixelShader9** out) {
    static_assert(N % sizeof(DWORD) == 0, "the compiled shader is not a whole number of tokens");
    DWORD tokens[N / sizeof(DWORD)];
    std::memcpy(tokens, blob, N);
    return device->CreatePixelShader(tokens, out);
}

template <size_t N>
HRESULT CreateShader(IDirect3DDevice9* device, const BYTE (&blob)[N],
                     IDirect3DVertexShader9** out) {
    static_assert(N % sizeof(DWORD) == 0, "the compiled shader is not a whole number of tokens");
    DWORD tokens[N / sizeof(DWORD)];
    std::memcpy(tokens, blob, N);
    return device->CreateVertexShader(tokens, out);
}

}
