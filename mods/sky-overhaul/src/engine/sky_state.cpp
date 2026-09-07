#include "engine/sky_state.h"

#include "engine/log.h"
#include "engine/seqlock.h"
#include "fcse_api.h"

#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>

namespace {
    // Where the sun's direction sits inside the renderer's scene state. The submission is handed a
    // pointer to it, which is also how the state itself is recovered.
    constexpr size_t kSunDirection = 0x148;
    constexpr size_t kNightFactor = 0x1B8;

    // __thiscall with fourteen stack arguments, declared __fastcall because MSVC will not let a
    // free function be __thiscall. Only the sixth stack argument is read; the rest are named to
    // get the stack shape right, since the callee cleans it.
    using SubmitSunDiscFn = void(__fastcall*)(void* self, void* unused, uint32_t a2, uint32_t a3,
                                              uint32_t a4, uint32_t a5, uint32_t a6,
                                              const float* sunDirection, uint32_t a8, uint32_t a9,
                                              uint32_t a10, uint32_t a11, uint32_t a12,
                                              uint32_t a13, uint32_t a14, uint32_t a15);

    // The submission's prologue, up to its first call. Every byte is fixed - no absolute address
    // and no relative branch falls inside it - and it occurs once in each shipped build.
    FCSE::Relocation<SubmitSunDiscFn> g_submitSunDisc{FCSE::Pattern(
        "55 8B EC 83 E4 F0 81 EC 34 01 00 00 53 8B D9 8B 4B 14 8B 43 10 56 8B 75 08 57 "
        "89 4C 24 14 6A 00 8B CE 89 44 24 14")};

    SubmitSunDiscFn g_original = nullptr;

    // The game thread writes the snapshot and the render thread reads it.
    SkyOverhaul::Seqlock<SkyOverhaul::SkyState::Sun> g_sun;

    std::atomic<uint32_t> g_submitCount{0};

    void __fastcall SubmitSunDiscDetour(void* self, void* unused, uint32_t a2, uint32_t a3,
                                        uint32_t a4, uint32_t a5, uint32_t a6,
                                        const float* sunDirection, uint32_t a8, uint32_t a9,
                                        uint32_t a10, uint32_t a11, uint32_t a12, uint32_t a13,
                                        uint32_t a14, uint32_t a15) {
        if (sunDirection != nullptr) {
            const float length = std::sqrt(sunDirection[0] * sunDirection[0] +
                                           sunDirection[1] * sunDirection[1] +
                                           sunDirection[2] * sunDirection[2]);
            if (length > 0.0001f) {
                const auto* state = reinterpret_cast<const uint8_t*>(sunDirection) - kSunDirection;

                SkyOverhaul::SkyState::Sun sun;
                sun.direction[0] = sunDirection[0] / length;
                sun.direction[1] = sunDirection[1] / length;
                sun.direction[2] = sunDirection[2] / length;
                sun.night = *reinterpret_cast<const float*>(state + kNightFactor);
                g_sun.Publish(sun);

                g_submitCount.fetch_add(1, std::memory_order_relaxed);
            }
        }

        g_original(self, unused, a2, a3, a4, a5, a6, sunDirection, a8, a9, a10, a11, a12, a13, a14,
                   a15);
    }
}

bool SkyOverhaul::SkyState::Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_submitSunDisc) {
        api->Log("sun unavailable: the sun-disc submission was not found in this build");
        return false;
    }

    if (!api->Hook(reinterpret_cast<void*>(g_submitSunDisc.address()),
                   reinterpret_cast<void*>(&SubmitSunDiscDetour),
                   reinterpret_cast<void**>(&g_original))) {
        api->Log("sun unavailable: the sun-disc submission could not be hooked");
        return false;
    }

    Logf("sky: sun-disc submission hooked at 0x%08zX",
         static_cast<size_t>(g_submitSunDisc.address()));
    return true;
}

bool SkyOverhaul::SkyState::Latest(Sun& out) {
    return g_sun.Latest(out);
}

uint32_t SkyOverhaul::SkyState::SubmitCount() {
    return g_submitCount.load(std::memory_order_relaxed);
}

