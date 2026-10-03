// The gameplay input pass, as a frame on the game thread in which the player's pawn is in hand.
#include "engine/pawn_tick.h"

#include "fcse_api.h"

namespace {
    // CPawnInputListener::Update, up to the call after which ECX is the live pawn. That call is
    // wildcarded because DevTools mid-hooks it.
    FCSE::Relocation<uint8_t*> g_inputPass{FCSE::Pattern(
        "56 8B F1 74 0A 88 46 04 88 46 05 5E C2 08 00 8B 4E 20 3B C8 74 4A ?? ?? ?? ?? ?? F6 40 04 "
        "40 74 11")};

    constexpr size_t kLivePawn = 0x16;

    // The listener, in ESI: its look accumulators, vertical then horizontal.
    constexpr ptrdiff_t kListenerLook = 0x10;

    constexpr size_t kMaxTicks = 4;
    VehicleOverhaul::PawnTick::TickFn g_ticks[kMaxTicks]{};
    size_t g_tickCount = 0;

    void OnInputPass(FCSE_MidHookContext* ctx) {
        for (size_t i = 0; i < g_tickCount; ++i) {
            g_ticks[i](reinterpret_cast<void*>(ctx->ecx), reinterpret_cast<float*>(ctx->esi + kListenerLook));
        }
    }
}

namespace VehicleOverhaul::PawnTick {

bool Subscribe(TickFn tick) {
    if (g_tickCount == kMaxTicks) {
        return false;
    }
    if (g_tickCount == 0) {
        if (!g_inputPass) {
            FCSE::ApiPointer()->Log("pawn tick: the gameplay input pass was not found in this build");
            return false;
        }
        if (!FCSE::ApiPointer()->MidHook(g_inputPass.get() + kLivePawn, &OnInputPass)) {
            return false;
        }
    }
    g_ticks[g_tickCount++] = tick;
    return true;
}

}
