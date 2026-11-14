#include "engine/fog_tint.h"

#include <d3d9.h>

#include <algorithm>

namespace {
    // The heading the fog's colour ramp runs along, its near end, and the distance from there to
    // the far end. See docs/docs/engine-internals/sky-and-clouds.md for which end faces the sun.
    constexpr UINT kFogVector = 48;
    constexpr UINT kFogColour = 49;
    constexpr UINT kFogRegisters = 3;

    struct Dusk {
        float dusk;
        float brightness;
        // Toward the sun, flat.
        float sun[2];
    };

    // One of the two constant files the fog is carried in: its three registers as the engine last
    // set them and as we last wrote them, and whether the device holds ours.
    struct Path {
        bool pixel;
        float engine[kFogRegisters * 4];
        float written[kFogRegisters * 4];
        bool ours;
    };

    // Published by the sky from inside the dome's draw, on the thread every draw runs on.
    Dusk g_dusk = {};
    Path g_vertex = {false};
    Path g_pixel = {true};

    // The colour and range registers, from `registers` laid out as the three are.
    void Set(IDirect3DDevice9* device, const Path& path, const float* registers) {
        if (path.pixel) {
            device->SetPixelShaderConstantF(kFogColour, registers + 4, 2);
        } else {
            device->SetVertexShaderConstantF(kFogColour, registers + 4, 2);
        }
    }

    // Writes the dusk's fog over the engine's, or puts the engine's back once there is none.
    void Write(IDirect3DDevice9* device, Path& path) {
        if (g_dusk.dusk <= 0.0f) {
            if (path.ours) {
                Set(device, path, path.engine);
                path.ours = false;
            }
            return;
        }

        // The end of the ramp toward the sun turns to the colour of the other end, and both dim.
        const float* engine = path.engine + 4;
        const bool sunNear =
            path.engine[0] * g_dusk.sun[0] + path.engine[1] * g_dusk.sun[1] >= 0.0f;
        const float shade = 1.0f + (g_dusk.brightness - 1.0f) * g_dusk.dusk;
        const float nearMoves = sunNear ? g_dusk.dusk : 0.0f;
        std::copy_n(path.engine, kFogRegisters * 4, path.written);
        for (size_t i = 0; i < 3; i++) {
            path.written[4 + i] = (engine[i] + engine[4 + i] * nearMoves) * shade;
            path.written[8 + i] = engine[4 + i] * (1.0f - g_dusk.dusk) * shade;
        }
        Set(device, path, path.written);
        path.ours = true;
    }

    // A register the draw is about to find other than the engine and we last left it is taken as
    // the engine's, and the colours are written over again. The engine uploads the three registers
    // together from some places and one at a time from others, so each is compared on its own.
    void Check(IDirect3DDevice9* device, Path& path) {
        const bool dusky = g_dusk.dusk > 0.0f;
        if (!dusky && !path.ours) {
            return;
        }
        float current[kFogRegisters * 4];
        const HRESULT read =
            path.pixel ? device->GetPixelShaderConstantF(kFogVector, current, kFogRegisters)
                       : device->GetVertexShaderConstantF(kFogVector, current, kFogRegisters);
        if (FAILED(read)) {
            return;
        }
        const float* left = path.ours ? path.written : path.engine;
        bool changed = dusky != path.ours;
        for (UINT i = 0; i < kFogRegisters; i++) {
            const float* now = current + i * 4;
            if (!std::equal(now, now + 4, left + i * 4)) {
                std::copy_n(now, 4, path.engine + i * 4);
                changed = true;
            }
        }
        if (changed) {
            Write(device, path);
        }
    }
}

void SkyOverhaul::FogTint::SetDusk(float dusk, float brightness, const float sun[3]) {
    g_dusk = {dusk, brightness, {sun[0], sun[1]}};
}

void SkyOverhaul::FogTint::BeforeDraw(IDirect3DDevice9* device) {
    Check(device, g_vertex);
    Check(device, g_pixel);
}

void SkyOverhaul::FogTint::Engine(float colour[3], float range[3]) {
    if (g_vertex.ours) {
        std::copy_n(g_vertex.engine + 4, 3, colour);
        std::copy_n(g_vertex.engine + 8, 3, range);
    }
}

void SkyOverhaul::FogTint::Forget() {
    g_dusk.dusk = 0.0f;
}
