#include "engine/fog_tint.h"

#include "engine/log.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <d3d9.h>

namespace {
    constexpr size_t kSetVertexConstantSlot = 94;
    constexpr size_t kSetPixelConstantSlot = 109;

    // Where the engine keeps the camera and everything hung off it. The fog colour is the near end
    // of a ramp and the register after it the distance to the far end, which is read by heading
    // against the sun - so a horizon is warmer looking into the light than away from it.
    constexpr UINT kCameraBlock = 45;
    constexpr UINT kFogColour = 49;
    constexpr UINT kFogColourRange = 50;

    // What dusty air reads as once the colour has been taken out of it: a warm grey rather than a
    // neutral one, because dust absorbs blue harder than it absorbs red.
    constexpr float kDust[3] = {1.09f, 1.00f, 0.86f};

    using SetConstantFn = HRESULT(__stdcall*)(IDirect3DDevice9*, UINT, const float*, UINT);

    SetConstantFn g_originalVertex = nullptr;
    SetConstantFn g_originalPixel = nullptr;

    float g_dust = 0.0f;
    uint32_t g_tints = 0;

    // Keeps the brightness the engine chose and replaces only the hue, so the result still tracks
    // the hour, the weather and the world without knowing anything about any of them.
    void Dust(float* colour) {
        const float grey = colour[0] * 0.299f + colour[1] * 0.587f + colour[2] * 0.114f;
        for (size_t i = 0; i < 3; i++) {
            colour[i] += (grey * kDust[i] - colour[i]) * g_dust;
        }
    }

    // Sends the two registers again, retinted, rather than editing the upload on its way past. The
    // engine hands over a block whose length it chose, and rewriting a copy of all of it would mean
    // guessing how long that can be; two registers of our own cost one small upload and cannot
    // disturb anything the caller did not already write there.
    void Retint(IDirect3DDevice9* device, UINT start, const float* data, UINT count,
                SetConstantFn set) {
        if (g_dust <= 0.0f) {
            return;
        }
        // Only the camera block itself. A material free to use these registers for something of its
        // own would be writing them from its own upload, which starts well past c45.
        if (start > kCameraBlock || start + count <= kFogColourRange) {
            return;
        }

        const float* colour = data + (kFogColour - start) * 4;
        const float* range = data + (kFogColourRange - start) * 4;

        // Both ends of the ramp are dusted and the ramp rebuilt between them, because the second
        // register is the distance from one colour to another rather than a colour itself.
        float toward[3];
        float away[3];
        for (size_t i = 0; i < 3; i++) {
            toward[i] = colour[i];
            away[i] = colour[i] + range[i];
        }
        Dust(toward);
        Dust(away);

        const float tinted[8] = {toward[0],
                                 toward[1],
                                 toward[2],
                                 colour[3],
                                 away[0] - toward[0],
                                 away[1] - toward[1],
                                 away[2] - toward[2],
                                 range[3]};
        set(device, kFogColour, tinted, 2);
        g_tints++;
    }

    HRESULT __stdcall SetVertexConstantDetour(IDirect3DDevice9* device, UINT start,
                                              const float* data, UINT count) {
        const HRESULT result = g_originalVertex(device, start, data, count);
        Retint(device, start, data, count, g_originalVertex);
        return result;
    }

    HRESULT __stdcall SetPixelConstantDetour(IDirect3DDevice9* device, UINT start,
                                             const float* data, UINT count) {
        const HRESULT result = g_originalPixel(device, start, data, count);
        Retint(device, start, data, count, g_originalPixel);
        return result;
    }

    bool Take(size_t slot, bool pixel, void* detour, SetConstantFn* original) {
        if (!SkyOverhaul::Vtable::IsConstantSetter(slot, pixel)) {
            SkyOverhaul::Logf("fog: slot %u is not the %s constant setter, so it is left alone",
                              static_cast<unsigned>(slot), pixel ? "pixel" : "vertex");
            return false;
        }
        return FCSE::ApiPointer()->Hook(SkyOverhaul::Vtable::Slot(slot), detour,
                                        reinterpret_cast<void**>(original));
    }
}

bool SkyOverhaul::FogTint::Install() {
    const bool vertex = Take(kSetVertexConstantSlot, false,
                             reinterpret_cast<void*>(&SetVertexConstantDetour), &g_originalVertex);
    const bool pixel = Take(kSetPixelConstantSlot, true,
                            reinterpret_cast<void*>(&SetPixelConstantDetour), &g_originalPixel);
    if (!vertex && !pixel) {
        Logf("fog: neither constant setter could be followed, so the world keeps its own colour");
        return false;
    }
    Logf("fog: following the %s%s%s constant upload", vertex ? "vertex" : "",
         vertex && pixel ? " and " : "", pixel ? "pixel" : "");
    return true;
}

void SkyOverhaul::FogTint::SetDust(int percent) {
    g_dust = static_cast<float>(percent) * 0.01f;
}

uint32_t SkyOverhaul::FogTint::TintCount() {
    return g_tints;
}
