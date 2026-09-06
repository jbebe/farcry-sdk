// The sun's direction, read out of the engine as it draws the sun.
//
// No shader is given the sun's direction as a global - the engine binds 46 constants to every
// shader and the sun is not among them, because the only draws that need it are already positioned
// at the sun. A screen-space effect is not one of those, so the direction has to come from the game
// side.
//
// The sky renderer (Steam Dunia.dll 0x1037A150) hands it to the sun-disc draw as the direction to
// orient the disc by:
//
//     state = GetSceneState();
//     ...
//     DrawSunDisc(..., state + 0x148, *(state + 0x170) /* SunRange */,
//                      *(state + 0x178) /* SunMaxHorizontalScale */,
//                      *(state + 0x17C) /* SunMaxVerticalScale */, ...);
//
// which the values beside it confirm: 0x170, 0x178 and 0x17C are three of the numbers
// CSky::LoadSky parses out of the world's <Sky> element, under exactly those names.
//
// The disc draw is hooked rather than the accessor above it. That accessor is one instantiation of
// a template the renderer uses for every kind of scene component - it occurs three times in each
// shipped build with identical bytes - so hooking it would be both ambiguous and liable to hand
// back some other subsystem's state. Arriving through the draw makes it the sky's by construction,
// and makes "did the engine draw a sky this frame" answerable at the same time.
#include "engine/sky_state.h"

#include "engine/sun_occlusion.h"

#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <d3d9.h>

namespace {
    // Where the renderer keeps the sun's direction inside the scene state, from the sky renderer's
    // use of it. The pointer it passes is the state plus this, so it also recovers the state.
    constexpr size_t kSunDirection = 0x148;

    // __thiscall with fourteen stack arguments, declared __fastcall because MSVC will not let a
    // free function be __thiscall. ECX carries `this`, EDX is unused, and the fourteen arguments
    // are on the stack in both conventions, so the callee cleans the same 56 bytes either way.
    // Only the sixth is read; the rest are named to get that count, and the stack shape, right.
    using DrawSunDiscFn = void(__fastcall*)(void* self, void* unused, uint32_t a2, uint32_t a3,
                                            uint32_t a4, uint32_t a5, uint32_t a6,
                                            const float* sunDirection, uint32_t a8, uint32_t a9,
                                            uint32_t a10, uint32_t a11, uint32_t a12, uint32_t a13,
                                            uint32_t a14, uint32_t a15);

    // The disc draw's prologue, up to its first call. Every byte is fixed - no absolute address and
    // no relative branch falls inside it - and it occurs once in each shipped build.
    FCSE::Relocation<DrawSunDiscFn> g_drawSunDisc{FCSE::Pattern(
        "55 8B EC 83 E4 F0 81 EC 34 01 00 00 53 8B D9 8B 4B 14 8B 43 10 56 8B 75 08 57 "
        "89 4C 24 14 6A 00 8B CE 89 44 24 14")};

    DrawSunDiscFn g_original = nullptr;

    // Written on the game thread, read on the presenting one. A torn read costs one frame of a
    // slightly wrong angle, which is invisible, so this is deliberately not synchronised.
    volatile bool g_haveSun = false;
    float g_sun[3] = {0.0f, 0.0f, 1.0f};

    // Raised every time the engine draws the sun and cleared by whoever asks, so a frame with no
    // sky in it is distinguishable from one the player is looking at the world through.
    volatile bool g_sunDrawn = false;

    IDirect3DDevice9* g_device = nullptr;

    void __fastcall DrawSunDiscDetour(void* self, void* unused, uint32_t a2, uint32_t a3,
                                      uint32_t a4, uint32_t a5, uint32_t a6,
                                      const float* sunDirection, uint32_t a8, uint32_t a9,
                                      uint32_t a10, uint32_t a11, uint32_t a12, uint32_t a13,
                                      uint32_t a14, uint32_t a15) {
        if (sunDirection != nullptr) {
            const float length = std::sqrt(sunDirection[0] * sunDirection[0] +
                                           sunDirection[1] * sunDirection[1] +
                                           sunDirection[2] * sunDirection[2]);
            if (length > 0.0001f) {
                g_sun[0] = sunDirection[0] / length;
                g_sun[1] = sunDirection[1] / length;
                g_sun[2] = sunDirection[2] / length;
                g_haveSun = true;
                g_sunDrawn = true;

                // Measured here, with this frame's direction, because this is the only point where
                // the scene's depth buffer is still attached to test against.
                SkyOverhaul::SunOcclusion::Sample(g_device, g_sun);
            }
        }

        g_original(self, unused, a2, a3, a4, a5, a6, sunDirection, a8, a9, a10, a11, a12, a13, a14,
                   a15);
    }
}

bool SkyOverhaul::SkyState::Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_drawSunDisc) {
        api->Log("sun direction unavailable: the sun-disc draw was not found in this build");
        return false;
    }

    if (!api->Hook(reinterpret_cast<void*>(g_drawSunDisc.address()),
                   reinterpret_cast<void*>(&DrawSunDiscDetour),
                   reinterpret_cast<void**>(&g_original))) {
        api->Log("sun direction unavailable: the sun-disc draw could not be hooked");
        return false;
    }
    return true;
}

bool SkyOverhaul::SkyState::SunDirection(float out[3]) {
    if (!g_haveSun) {
        return false;
    }
    out[0] = g_sun[0];
    out[1] = g_sun[1];
    out[2] = g_sun[2];
    return true;
}

bool SkyOverhaul::SkyState::ConsumeSunDrawn() {
    const bool drawn = g_sunDrawn;
    g_sunDrawn = false;
    return drawn;
}

void SkyOverhaul::SkyState::SetDevice(IDirect3DDevice9* device) {
    g_device = device;
}

float SkyOverhaul::SkyState::SunVisibility() {
    return SkyOverhaul::SunOcclusion::Visibility();
}
