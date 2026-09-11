#include "engine/fog_tint.h"

#include "engine/seqlock.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <d3d9.h>

namespace {
    constexpr size_t kSetVertexConstantSlot = 94;
    constexpr size_t kSetPixelConstantSlot = 109;

    // The near end of the fog's colour ramp and the distance from it to the far end. The engine
    // reads the ramp by heading against its own fog vector, so a horizon is one colour looking
    // along that vector and another looking against it.
    constexpr UINT kFogColour = 49;
    constexpr UINT kFogColourRange = 50;

    using SetConstantFn = HRESULT(__stdcall*)(IDirect3DDevice9*, UINT, const float*, UINT);

    SetConstantFn g_originalVertex = nullptr;
    SetConstantFn g_originalPixel = nullptr;

    struct Horizon {
        float toward[3];
        float away[3];
        float match;
    };

    // Written once a frame by the sky and read on whatever thread uploads constants.
    SkyOverhaul::Seqlock<Horizon> g_horizon;
    bool g_have = false;
    uint32_t g_tints = 0;

    // Sends the two registers again rather than editing the upload on its way past. The engine
    // hands over a block whose length it chose, and rewriting a copy of all of it would mean
    // guessing how long that can be; two registers of our own cost one small upload and cannot
    // disturb anything the caller was not already writing there.
    void Replace(IDirect3DDevice9* device, UINT start, const float* data, UINT count,
                 SetConstantFn set) {
        if (!g_have) {
            return;
        }
        // Any upload that covers both ends of the ramp, wherever it starts. The engine sets these
        // from more than one place and not every one of them begins at the camera block, which is
        // what a narrower test missed: the colour was replaced while the world loaded and written
        // over on every frame after it.
        if (start > kFogColour || start + count <= kFogColourRange) {
            return;
        }

        Horizon horizon;
        if (!g_horizon.Latest(horizon) || horizon.match <= 0.0f) {
            return;
        }

        const float* colour = data + (kFogColour - start) * 4;
        const float* range = data + (kFogColourRange - start) * 4;

        // Both ends are moved and the ramp rebuilt between them, because the second register is the
        // distance from one colour to another rather than a colour itself.
        float toward[3];
        float away[3];
        for (size_t i = 0; i < 3; i++) {
            toward[i] = colour[i] + (horizon.toward[i] - colour[i]) * horizon.match;
            away[i] =
                (colour[i] + range[i]) + (horizon.away[i] - (colour[i] + range[i])) * horizon.match;
        }

        const float replaced[8] = {toward[0],
                                   toward[1],
                                   toward[2],
                                   colour[3],
                                   away[0] - toward[0],
                                   away[1] - toward[1],
                                   away[2] - toward[2],
                                   range[3]};
        set(device, kFogColour, replaced, 2);
        g_tints++;
    }

    HRESULT __stdcall SetVertexConstantDetour(IDirect3DDevice9* device, UINT start,
                                              const float* data, UINT count) {
        const HRESULT result = g_originalVertex(device, start, data, count);
        Replace(device, start, data, count, g_originalVertex);
        return result;
    }

    HRESULT __stdcall SetPixelConstantDetour(IDirect3DDevice9* device, UINT start,
                                             const float* data, UINT count) {
        const HRESULT result = g_originalPixel(device, start, data, count);
        Replace(device, start, data, count, g_originalPixel);
        return result;
    }

    bool Take(size_t slot, bool pixel, void* detour, SetConstantFn* original) {
        if (!SkyOverhaul::Vtable::IsConstantSetter(slot, pixel)) {
            FCSE::Logf("fog: slot %u is not the %s constant setter, so it is left alone",
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
        FCSE::Logf("fog: neither constant setter could be followed, so the world keeps its own "
                   "colour");
        return false;
    }
    FCSE::Logf("fog: following the %s%s%s constant upload", vertex ? "vertex" : "",
               vertex && pixel ? " and " : "", pixel ? "pixel" : "");
    return true;
}

void SkyOverhaul::FogTint::SetHorizon(const float toward[3], const float away[3], float match) {
    Horizon horizon;
    for (size_t i = 0; i < 3; i++) {
        horizon.toward[i] = toward[i];
        horizon.away[i] = away[i];
    }
    horizon.match = match;
    g_horizon.Publish(horizon);
    g_have = true;
}

void SkyOverhaul::FogTint::Forget() {
    g_have = false;
}

uint32_t SkyOverhaul::FogTint::TintCount() {
    return g_tints;
}
