#include "engine/fog_tint.h"

#include "engine/seqlock.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <d3d9.h>

#include <algorithm>

namespace {
    constexpr size_t kSetVertexConstantSlot = 94;
    constexpr size_t kSetPixelConstantSlot = 109;

    // The near end of the fog's colour ramp and the distance from it to the far end. The engine
    // reads the ramp by heading against its own fog vector, so a horizon is one colour looking
    // along that vector and another looking against it.
    constexpr UINT kFogColour = 49;
    constexpr UINT kFogColourRange = 50;

    // How much darker than the engine's own fog the land is kept.
    constexpr float kShade = 0.6f;
    // The most one channel of the sky's hue may ask for.
    constexpr float kHueCap = 3.0f;
    // A sky darker than this has no hue to lend.
    constexpr float kHuelessSky = 1.0e-3f;

    using SetConstantFn = HRESULT(__stdcall*)(IDirect3DDevice9*, UINT, const float*, UINT);

    SetConstantFn g_originalVertex = nullptr;
    SetConstantFn g_originalPixel = nullptr;

    // The sky's horizon hue at a luminance of one, along the fog heading and against it, and whether
    // each end has one.
    struct Horizon {
        float towardHue[3];
        float awayHue[3];
        bool towardHasHue;
        bool awayHasHue;
    };

    // Written once a frame by the sky and read on whatever thread uploads constants.
    SkyOverhaul::Seqlock<Horizon> g_horizon;
    bool g_have = false;
    uint32_t g_tints = 0;

    float Luminance(const float colour[3]) {
        return colour[0] * 0.299f + colour[1] * 0.587f + colour[2] * 0.114f;
    }

    // A colour at a luminance of one, each channel capped. False, with nothing written, for a colour
    // too dark to have a hue.
    bool Hue(const float colour[3], float out[3]) {
        const float luminance = Luminance(colour);
        if (luminance < kHuelessSky) {
            return false;
        }
        for (size_t i = 0; i < 3; i++) {
            const float hue = colour[i] / luminance;
            out[i] = hue < kHueCap ? hue : kHueCap;
        }
        return true;
    }

    // The engine's fog colour at its own brightness in the sky's hue, shaded, or the engine's own
    // where the sky has no hue.
    void Shade(const float engine[3], const float hue[3], bool hasHue, float out[3]) {
        if (!hasHue) {
            std::copy_n(engine, 3, out);
            return;
        }
        const float brightness = Luminance(engine) * kShade;
        for (size_t i = 0; i < 3; i++) {
            out[i] = hue[i] * brightness;
        }
    }

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
        if (!g_horizon.Latest(horizon)) {
            return;
        }

        const float* colour = data + (kFogColour - start) * 4;
        const float* range = data + (kFogColourRange - start) * 4;
        const float engineAway[3] = {colour[0] + range[0], colour[1] + range[1],
                                     colour[2] + range[2]};

        float toward[3];
        float away[3];
        Shade(colour, horizon.towardHue, horizon.towardHasHue, toward);
        Shade(engineAway, horizon.awayHue, horizon.awayHasHue, away);

        // Both ends are replaced and the ramp rebuilt between them, because the second register is
        // the distance from one colour to another rather than a colour itself.
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

void SkyOverhaul::FogTint::SetHorizon(const float toward[3], const float away[3]) {
    Horizon horizon = {};
    horizon.towardHasHue = Hue(toward, horizon.towardHue);
    horizon.awayHasHue = Hue(away, horizon.awayHue);
    g_horizon.Publish(horizon);
    g_have = true;
}

void SkyOverhaul::FogTint::Forget() {
    g_have = false;
}

uint32_t SkyOverhaul::FogTint::TintCount() {
    return g_tints;
}
