// The gameplay input pass, as somewhere to run code that needs the player.
//
// CPawnInputListener::Update; the pattern reaches the point where the listener is in ESI and the
// live pawn in ECX. See docs/docs/engine-internals/presentation-and-input.md.
#include "engine/pawn_tick.h"

#include "engine/keys.h"
#include "engine/player.h"
#include "fcse_api.h"

#include <chrono>

namespace {
    FCSE::Relocation<uint8_t*> g_inputPass{FCSE::Pattern(
        "56 8B F1 74 0A 88 46 04 88 46 05 5E C2 08 00 8B 4E 20 3B C8 74 4A E8 ?? ?? ?? ?? F6 40 04 "
        "40 74 11")};

    // Where in that pattern the pawn is live.
    constexpr size_t kLivePawn = 0x16;

    // A hitch or a load leaves an arbitrary gap; a step nothing could have moved through is safer
    // than one that teleports.
    constexpr float kMaxDelta = 0.25f;

    constexpr size_t kMaxSubscribers = 8;
    DevTools::PawnTick::TickFn g_subscribers[kMaxSubscribers]{};
    size_t g_subscriberCount = 0;

    std::chrono::steady_clock::time_point g_last;
    bool g_haveLast = false;

    // The input pass carries no frame time, so it is measured here.
    float Delta() {
        const auto now = std::chrono::steady_clock::now();
        if (!g_haveLast) {
            g_haveLast = true;
            g_last = now;
            return 0.0f;
        }

        const float delta = std::chrono::duration<float>(now - g_last).count();
        g_last = now;
        return (delta < 0.0f || delta > kMaxDelta) ? 0.0f : delta;
    }

    void MidHookHandler(FCSE_MidHookContext* ctx) {
        const DevTools::PawnTick::Frame frame{ctx->esi, ctx->ecx, Delta(), DevTools::Player::Local(),
                                              DevTools::Keys::Focused()};

        for (size_t i = 0; i < g_subscriberCount; ++i) {
            g_subscribers[i](frame);
        }
    }
}

namespace DevTools::PawnTick {

void Subscribe(TickFn fn) {
    if (g_subscriberCount < kMaxSubscribers) {
        g_subscribers[g_subscriberCount++] = fn;
    }
}

void Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_inputPass) {
        api->Log("pawn tick: the gameplay input pass was not found in this build - nothing that "
                 "needs the player will run");
        return;
    }

    api->MidHook(g_inputPass.get() + kLivePawn, &MidHookHandler);
}

}
