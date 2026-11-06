#include "engine/sun.h"

#include "engine/memory.h"
#include "engine/seqlock.h"
#include "fcse_api.h"

#include <atomic>
#include <cmath>

namespace {
    using PlayerBody::Field;

    // In the renderer's scene state: the storm, the direction the shadows are cast along, away from
    // the light, and the night.
    constexpr ptrdiff_t kStorm = 0x78;
    constexpr ptrdiff_t kShadowDirection = 0x328;
    constexpr ptrdiff_t kNight = 0x1B8;

    // __thiscall with twelve stack arguments, of which the sixth is the scene state.
    using SubmitFn = void(__fastcall*)(void* self, void* unused, uint32_t a2, uint32_t a3, uint32_t a4,
                                       uint32_t a5, uint32_t a6, uint8_t* state, uint32_t a8,
                                       uint32_t a9, uint32_t a10, uint32_t a11, uint32_t a12,
                                       uint32_t a13);
    FCSE::Relocation<SubmitFn> g_submit{FCSE::Uplay(0x003DC3C0)};
    SubmitFn g_originalSubmit = nullptr;

    PlayerBody::Seqlock<PlayerBody::Sun::Light> g_light;
    std::atomic<uint32_t> g_submissions{0};

    void __fastcall SubmitDetour(void* self, void* unused, uint32_t a2, uint32_t a3, uint32_t a4,
                                 uint32_t a5, uint32_t a6, uint8_t* state, uint32_t a8, uint32_t a9,
                                 uint32_t a10, uint32_t a11, uint32_t a12, uint32_t a13) {
        if (state != nullptr) {
            const float* away = &Field<float>(state, kShadowDirection);
            const float length = std::sqrt(away[0] * away[0] + away[1] * away[1] + away[2] * away[2]);
            if (length > 0.0001f) {
                g_light.Publish({{-away[0] / length, -away[1] / length, -away[2] / length},
                                 Field<float>(state, kStorm), Field<float>(state, kNight)});
            }
            g_submissions.fetch_add(1, std::memory_order_relaxed);
        }
        g_originalSubmit(self, unused, a2, a3, a4, a5, a6, state, a8, a9, a10, a11, a12, a13);
    }
}

bool PlayerBody::Sun::Install() {
    if (!g_submit) {
        FCSE::ApiPointer()->Log("sun: the cloud-layer submission was not found");
        return false;
    }
    return Hook(g_submit.address(), &SubmitDetour, &g_originalSubmit);
}

bool PlayerBody::Sun::Latest(Light& out) {
    return g_light.Latest(out);
}

uint32_t PlayerBody::Sun::Submissions() {
    return g_submissions.load(std::memory_order_relaxed);
}
