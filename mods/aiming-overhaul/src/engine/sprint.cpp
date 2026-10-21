// The player's sprint press, which can be refused while down the sights.
#include "engine/sprint.h"

#include "engine/memory.h"
#include "engine/pawn.h"
#include "fcse_api.h"

#include <cstddef>
#include <cstdint>

namespace {
    using AimingOverhaul::Field;

    // In CPawnInputListener's signal handler, the call for the desired data before OR byte
    // [EAX+4],40h, every frame a sprint key is held; EAX is the signal and ESI the listener. The
    // call is a wildcard because Legacy Plugin's sprint toggle hooks it too.
    FCSE::Relocation<uint8_t*> g_press{
        FCSE::Pattern("?? ?? ?? ?? ?? 80 48 04 40 5F 5E 83 C4 10 C2 08 00")};
    // Past the call and the OR, to the handler's return.
    constexpr ptrdiff_t kPastPress = 9;

    AimingOverhaul::Sprint::RefuseFn g_refuse = nullptr;

    void OnPress(FCSE_MidHookContext* ctx) {
        uint8_t* pawn = Field<uint8_t*>(reinterpret_cast<uint8_t*>(ctx->esi),
                                        AimingOverhaul::Pawn::kListenerPawn);
        if (pawn != nullptr && AimingOverhaul::Pawn::Is(pawn, AimingOverhaul::Pawn::kIronsight) &&
            g_refuse()) {
            ctx->eip = g_press.address() + kPastPress;
        }
    }
}

bool AimingOverhaul::Sprint::Install(RefuseFn refuse) {
    if (!g_press) {
        FCSE::ApiPointer()->Log("sprint: the sprint press was not found in this build");
        return false;
    }
    g_refuse = refuse;
    if (!FCSE::ApiPointer()->MidHook(g_press.get(), &OnPress)) {
        FCSE::ApiPointer()->Log("sprint: the sprint press could not be hooked");
        return false;
    }
    return true;
}
