#include "engine/fog_tint.h"

#include "engine/seqlock.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <d3d9.h>

#include <algorithm>

namespace {
    constexpr size_t kSetVertexConstantSlot = 94;
    constexpr size_t kSetPixelConstantSlot = 109;

    // The heading the fog's colour ramp runs along, its near end, and the distance from there to
    // the far end. See docs/docs/engine-internals/sky-and-clouds.md for which end faces the sun.
    constexpr UINT kFogVector = 48;
    constexpr UINT kFogColour = 49;
    constexpr UINT kFogColourRange = 50;
    constexpr UINT kFogRegisters = 3;

    using SetConstantFn = HRESULT(__stdcall*)(IDirect3DDevice9*, UINT, const float*, UINT);

    SetConstantFn g_originalVertex = nullptr;
    SetConstantFn g_originalPixel = nullptr;

    struct Dusk {
        float dusk;
        float brightness;
        // Toward the sun, flat.
        float sun[2];
    };

    // One of the two constant files the fog is carried in: its three registers as the engine last
    // set them, the colour and range we wrote over them, and whether either holds ours now.
    struct Path {
        bool pixel;
        float engine[kFogRegisters * 4];
        bool have[kFogRegisters];
        float written[8];
        bool ours;
    };

    // Written once a frame by the sky and read on whatever thread uploads constants.
    SkyOverhaul::Seqlock<Dusk> g_dusk;
    // Whether the sun is set far enough for the land's fog to change, kept for the draws to test.
    bool g_dusky = false;
    Path g_vertex = {false};
    Path g_pixel = {true};

    HRESULT Set(IDirect3DDevice9* device, const Path& path, const float* values) {
        return (path.pixel ? g_originalPixel : g_originalVertex)(device, kFogColour, values, 2);
    }

    // Writes the dusk's fog over the engine's, or puts the engine's back once there is none, as two
    // registers of our own sent after the engine's upload.
    void Write(IDirect3DDevice9* device, Path& path) {
        if (!path.have[0] || !path.have[1] || !path.have[2]) {
            return;
        }
        const float* engine = path.engine + 4;
        Dusk dusk;
        if (!g_dusky || !g_dusk.Latest(dusk) || dusk.dusk <= 0.0f) {
            if (path.ours) {
                Set(device, path, engine);
                path.ours = false;
            }
            return;
        }

        // The end of the ramp toward the sun turns to the colour of the other end, and both dim.
        const bool sunNear = path.engine[0] * dusk.sun[0] + path.engine[1] * dusk.sun[1] >= 0.0f;
        const float shade = 1.0f + (dusk.brightness - 1.0f) * dusk.dusk;
        const float nearMoves = sunNear ? dusk.dusk : 0.0f;
        for (size_t i = 0; i < 3; i++) {
            path.written[i] = (engine[i] + engine[4 + i] * nearMoves) * shade;
            path.written[4 + i] = engine[4 + i] * (1.0f - dusk.dusk) * shade;
        }
        path.written[3] = engine[3];
        path.written[7] = engine[7];
        Set(device, path, path.written);
        path.ours = true;
    }

    // The engine sets the three registers together from some places and one at a time from
    // others, so each is remembered as it last set it and the colour and range are written back
    // after any of them.
    void Replace(IDirect3DDevice9* device, UINT start, const float* data, UINT count, Path& path) {
        bool any = false;
        for (UINT i = 0; i < kFogRegisters; i++) {
            const UINT reg = kFogVector + i;
            if (start > reg || start + count <= reg) {
                continue;
            }
            std::copy_n(data + (reg - start) * 4, 4, path.engine + i * 4);
            path.have[i] = true;
            any = true;
        }
        if (!any) {
            return;
        }
        // Both colour registers are the engine's again only if it set both.
        if (start <= kFogColour && start + count > kFogColourRange) {
            path.ours = false;
        }
        Write(device, path);
    }

    // What a draw is about to find in the fog's registers, if it is not what the engine and we last
    // left there, is taken as the engine's and written over again.
    void Check(IDirect3DDevice9* device, Path& path) {
        if (!g_dusky && !path.ours) {
            return;
        }
        float current[kFogRegisters * 4];
        const HRESULT read =
            path.pixel ? device->GetPixelShaderConstantF(kFogVector, current, kFogRegisters)
                       : device->GetVertexShaderConstantF(kFogVector, current, kFogRegisters);
        if (FAILED(read)) {
            return;
        }
        const bool vectorKept = std::equal(current, current + 4, path.engine);
        const bool coloursKept =
            path.ours ? std::equal(current + 4, current + 12, path.written)
                      : std::equal(current + 4, current + 12, path.engine + 4);
        if (vectorKept && coloursKept && g_dusky == path.ours) {
            return;
        }
        std::copy_n(current, 4, path.engine);
        if (!coloursKept) {
            std::copy_n(current + 4, 8, path.engine + 4);
            path.ours = false;
        }
        std::fill(path.have, path.have + kFogRegisters, true);
        Write(device, path);
    }

    HRESULT __stdcall SetVertexConstantDetour(IDirect3DDevice9* device, UINT start,
                                              const float* data, UINT count) {
        const HRESULT result = g_originalVertex(device, start, data, count);
        Replace(device, start, data, count, g_vertex);
        return result;
    }

    HRESULT __stdcall SetPixelConstantDetour(IDirect3DDevice9* device, UINT start,
                                             const float* data, UINT count) {
        const HRESULT result = g_originalPixel(device, start, data, count);
        Replace(device, start, data, count, g_pixel);
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
    return true;
}

void SkyOverhaul::FogTint::SetDusk(float dusk, float brightness, const float sun[3]) {
    g_dusk.Publish({dusk, brightness, {sun[0], sun[1]}});
    g_dusky = dusk > 0.0f;
}

void SkyOverhaul::FogTint::BeforeDraw(IDirect3DDevice9* device) {
    Check(device, g_vertex);
    Check(device, g_pixel);
}

void SkyOverhaul::FogTint::Engine(float colour[3], float range[3]) {
    if (g_vertex.have[1]) {
        std::copy_n(g_vertex.engine + 4, 3, colour);
    }
    if (g_vertex.have[2]) {
        std::copy_n(g_vertex.engine + 8, 3, range);
    }
}

void SkyOverhaul::FogTint::Forget() {
    g_dusky = false;
}
