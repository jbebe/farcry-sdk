// The game's slide behind a turn, handed to the animation as nought. See turn_slide.h.
#include "turn_slide.h"

#include "fcse_api.h"

#include <atomic>
#include <cstddef>
#include <cstdint>

namespace {
    // In UpdatePlayerMoveState: PUSH ECX for the argument, the rate copied to the skills, then
    // FLD [EBX+58h]; FSTP [ESP] puts the eased turn rate in it for channel 28, PlayerAngularSpeed.
    FCSE::Relocation<uint8_t*> g_slide{
        FCSE::Pattern("51 F3 0F 11 40 58 D9 43 58 D9 1C 24 6A 1C 8B CE E8")};
    // From the pattern to the FLD; the FLD and FSTP take as many bytes again.
    constexpr ptrdiff_t kLoad = 6;

    std::atomic<bool> g_taken{false};

    void OnSlide(FCSE_MidHookContext* ctx) {
        if (g_taken) {
            *reinterpret_cast<float*>(ctx->esp) = 0.0f;
            ctx->eip = g_slide.address() + 2 * kLoad;
        }
    }
}

bool AimingOverhaul::TurnSlide::Install() {
    if (!g_slide) {
        FCSE::ApiPointer()->Log("turn slide: the arms' slide behind a turn was not found in this build");
        return false;
    }
    if (!FCSE::ApiPointer()->MidHook(g_slide.get() + kLoad, &OnSlide)) {
        FCSE::ApiPointer()->Log("turn slide: the arms' slide behind a turn could not be hooked");
        return false;
    }
    return true;
}

void AimingOverhaul::TurnSlide::SetTaken(bool taken) {
    g_taken = taken;
}
