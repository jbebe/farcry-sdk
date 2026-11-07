// The player's pawn. See docs/docs/engine-internals/first-person-aiming.md.
#include "engine/pawn.h"

#include "fcse_api.h"

namespace {
    // CPawnInputListener::Update, up to the call after which ECX is the live pawn. That call is
    // wildcarded because other plugins mid-hook it.
    FCSE::Relocation<uint8_t*> g_inputPass{FCSE::Pattern(
        "56 8B F1 74 0A 88 46 04 88 46 05 5E C2 08 00 8B 4E 20 3B C8 74 4A ?? ?? ?? ?? ?? F6 40 04 "
        "40 74 11")};
    constexpr size_t kLivePawn = 0x16;

    // CVehicle::GetCurrentVehicle: the vehicle a pawn sits in, or null.
    using GetCurrentVehicleFn = void*(__cdecl*)(uint8_t* pawn);
    FCSE::Relocation<GetCurrentVehicleFn> g_currentVehicle{FCSE::Uplay(0x000E7330)};

    // CPawn::ViewModeChanged, __thiscall, found through its lookup of the pawn's graphic component.
    using ViewModeChangedFn = void(__fastcall*)(uint8_t* pawn, void* unused, bool firstPerson);
    FCSE::Relocation<ViewModeChangedFn> g_viewModeChanged{FCSE::Pattern(
        "53 8B 5C 24 08 55 56 57 53 8B F9 E8 ?? ?? ?? ?? 8B 77 08 8B 6E 0C 83 46 08 01 8B CD E8 ?? ?? ?? ?? "
        "83 3D ?? ?? ?? ?? 00 75 07 33 C9 E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? 8B CD E8")};

    PlayerBody::Pawn::TickFn g_tick = nullptr;

    void OnInputPass(FCSE_MidHookContext* ctx) {
        g_tick(reinterpret_cast<uint8_t*>(ctx->ecx));
    }

    void SetFirstPersonBit(uint8_t& flags, bool firstPerson) {
        flags = firstPerson ? flags | PlayerBody::Pawn::kFirstPerson
                            : flags & ~PlayerBody::Pawn::kFirstPerson;
    }
}

bool PlayerBody::Pawn::Install(TickFn tick) {
    if (!g_inputPass || !g_currentVehicle) {
        FCSE::ApiPointer()->Log("pawn: the gameplay input pass or the seat lookup was not found");
        return false;
    }
    g_tick = tick;
    return FCSE::ApiPointer()->MidHook(g_inputPass.get() + kLivePawn, &OnInputPass);
}

uint8_t* PlayerBody::Pawn::Entity(uint8_t* component) {
    return static_cast<uint8_t*>(FCSE::ApiPointer()->EntityData->EntityOf(component));
}

bool PlayerBody::Pawn::OnFoot(uint8_t* pawn) {
    return g_currentVehicle(pawn) == nullptr;
}

void PlayerBody::Pawn::SetFirstPerson(uint8_t* pawn, bool firstPerson) {
    if (!g_viewModeChanged) {
        FCSE::ApiPointer()->Log("pawn: CPawn::ViewModeChanged was not found");
        return;
    }
    SetFirstPersonBit(Field<uint8_t>(Data(pawn), kDesiredFlags), firstPerson);
    SetFirstPersonBit(Field<uint8_t>(Data(pawn), kEffectiveFlags), firstPerson);
    g_viewModeChanged(pawn, nullptr, firstPerson);
}
