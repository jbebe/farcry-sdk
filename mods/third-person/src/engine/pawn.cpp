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
    // The listener, in ESI there: its look accumulators.
    constexpr ptrdiff_t kListenerLook = 0x10;

    // CVehicle::GetCurrentVehicle: the vehicle a pawn sits in, or null.
    using GetCurrentVehicleFn = void*(__cdecl*)(uint8_t* pawn);
    FCSE::Relocation<GetCurrentVehicleFn> g_currentVehicle{FCSE::Uplay(0x000E7330)};

    // CPawn::ViewModeChanged, __thiscall, found through its lookup of the pawn's graphic component.
    using ViewModeChangedFn = void(__fastcall*)(uint8_t* pawn, void* unused, bool firstPerson);
    FCSE::Relocation<ViewModeChangedFn> g_viewModeChanged{FCSE::Pattern(
        "53 8B 5C 24 08 55 56 57 53 8B F9 E8 ?? ?? ?? ?? 8B 77 08 8B 6E 0C 83 46 08 01 8B CD E8 ?? ?? ?? ?? "
        "83 3D ?? ?? ?? ?? 00 75 07 33 C9 E8 ?? ?? ?? ?? 68 ?? ?? ?? ?? 8B CD E8")};

    // CInventoryViewPawn::GetGadgetFromCategory(&category) and GetEquippedGadget, __thiscall on the
    // view, which is in the pawn's data.
    using GadgetFn = uint8_t*(__fastcall*)(uint8_t* view, void* unused, const uint32_t* category);
    using EquippedGadgetFn = uint8_t*(__fastcall*)(uint8_t* view);
    FCSE::Relocation<GadgetFn> g_gadget{FCSE::Uplay(0x00129DF0)};
    FCSE::Relocation<EquippedGadgetFn> g_equippedGadget{FCSE::Uplay(0x00129E20)};
    constexpr ptrdiff_t kDataInventoryView = 0x4F0;

    // CEntity::Lock and CEntity::SetIsVisible, __thiscall on the entity.
    using LockFn = void(__fastcall*)(uint8_t* entity);
    using SetIsVisibleFn = void(__fastcall*)(uint8_t* entity, void* unused, bool visible);
    FCSE::Relocation<LockFn> g_lock{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<SetIsVisibleFn> g_setIsVisible{FCSE::Uplay(0x004DDB20)};

    ThirdPerson::Pawn::TickFn g_tick = nullptr;

    void OnInputPass(FCSE_MidHookContext* ctx) {
        g_tick(reinterpret_cast<uint8_t*>(ctx->ecx), reinterpret_cast<float*>(ctx->esi + kListenerLook));
    }

    void SetFirstPersonBit(uint8_t& flags, bool firstPerson) {
        flags = firstPerson ? flags | ThirdPerson::Pawn::kFirstPerson
                            : flags & ~ThirdPerson::Pawn::kFirstPerson;
    }
}

bool ThirdPerson::Pawn::Install(TickFn tick) {
    if (!g_inputPass || !g_currentVehicle) {
        FCSE::ApiPointer()->Log("pawn: the gameplay input pass or the seat lookup was not found");
        return false;
    }
    g_tick = tick;
    return FCSE::ApiPointer()->MidHook(g_inputPass.get() + kLivePawn, &OnInputPass);
}

uint8_t* ThirdPerson::Pawn::Entity(uint8_t* component) {
    return static_cast<uint8_t*>(FCSE::ApiPointer()->EntityData->EntityOf(component));
}

void ThirdPerson::Pawn::Lock(uint8_t* entity) {
    if (g_lock) {
        g_lock(entity);
    }
}

uint8_t* ThirdPerson::Pawn::Vehicle(uint8_t* pawn) {
    return static_cast<uint8_t*>(g_currentVehicle(pawn));
}

uint8_t* ThirdPerson::Pawn::Gadget(uint8_t* pawn, uint32_t category) {
    return g_gadget ? g_gadget(Data(pawn) + kDataInventoryView, nullptr, &category) : nullptr;
}

uint8_t* ThirdPerson::Pawn::EquippedGadget(uint8_t* pawn) {
    return g_equippedGadget ? g_equippedGadget(Data(pawn) + kDataInventoryView) : nullptr;
}

void ThirdPerson::Pawn::HideGadget(uint8_t* gadget) {
    uint8_t* entity = Entity(gadget);
    if (entity != nullptr && g_setIsVisible) {
        Lock(entity);
        g_setIsVisible(entity, nullptr, false);
    }
}

void ThirdPerson::Pawn::SetFirstPerson(uint8_t* pawn, bool firstPerson) {
    if (!g_viewModeChanged) {
        FCSE::ApiPointer()->Log("pawn: CPawn::ViewModeChanged was not found");
        return;
    }
    SetFirstPersonBit(Field<uint8_t>(Data(pawn), kDesiredFlags), firstPerson);
    SetFirstPersonBit(Field<uint8_t>(Data(pawn), kEffectiveFlags), firstPerson);
    g_viewModeChanged(pawn, nullptr, firstPerson);
}
