// Whether the player sits in a vehicle, read on the gameplay input pass, where the player's pawn is
// in hand.
#include "engine/vehicle.h"

#include "fcse_api.h"

#include <atomic>

namespace {
    using GetCurrentVehicleFn = void*(__cdecl*)(void* pawn);

    // CPawnInputListener::Update, up to the call after which ECX is the live pawn. That call is
    // wildcarded because DevTools and Vehicle Overhaul mid-hook it.
    FCSE::Relocation<uint8_t*> g_inputPass{FCSE::Pattern(
        "56 8B F1 74 0A 88 46 04 88 46 05 5E C2 08 00 8B 4E 20 3B C8 74 4A ?? ?? ?? ?? ?? F6 40 04 "
        "40 74 11")};
    constexpr size_t kLivePawn = 0x16;

    FCSE::Relocation<GetCurrentVehicleFn> g_getCurrentVehicle{FCSE::Uplay(0x000E7330)};

    std::atomic<bool> g_seated{false};

    void OnInputPass(FCSE_MidHookContext* ctx) {
        void* pawn = reinterpret_cast<void*>(ctx->ecx);
        g_seated = pawn != nullptr && g_getCurrentVehicle(pawn) != nullptr;
    }
}

namespace Flashlight::Vehicle {

void Install() {
    if (!g_inputPass || !g_getCurrentVehicle ||
        !FCSE::ApiPointer()->MidHook(g_inputPass.get() + kLivePawn, &OnInputPass)) {
        FCSE::ApiPointer()->Log("vehicle: the input pass could not be hooked - the light stays on in vehicles");
    }
}

bool Seated() { return g_seated; }

}
