// The player's pawn. See docs/docs/engine-internals/first-person-aiming.md#holding-the-breath.
#include "engine/pawn.h"

#include "fcse_api.h"

namespace {
    using AimingOverhaul::Field;

    // Both __thiscall, which a free function spells __fastcall with an unused EDX.
    using LockFn = void(__fastcall*)(void* entity, void* unused);
    using GetComponentFn = uint8_t*(__fastcall*)(void* entity, void* unused, const uint32_t* id);
    FCSE::Relocation<LockFn> g_lock{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<GetComponentFn> g_getComponent{FCSE::Uplay(0x004E2CC0)};

    constexpr uint32_t kCounters = FCSE::Crc32("CFCXCountersComponentPlayer");

    // A pawn's entity proxy and the proxy's entity; the counters' stamina, and in it the stamina
    // left and the most there can be.
    constexpr ptrdiff_t kPawnProxy = 0x08;
    constexpr ptrdiff_t kProxyEntity = 0x0C;
    constexpr ptrdiff_t kStamina = 0x64;
    constexpr ptrdiff_t kStaminaLeft = 0x10;
    constexpr ptrdiff_t kStaminaMost = 0x14;
}

float AimingOverhaul::Pawn::Stamina(uint8_t* pawn) {
    uint8_t* proxy = g_lock && g_getComponent ? Field<uint8_t*>(pawn, kPawnProxy) : nullptr;
    uint8_t* entity = proxy != nullptr ? Field<uint8_t*>(proxy, kProxyEntity) : nullptr;
    if (entity == nullptr) {
        return 1.0f;
    }
    g_lock(entity, nullptr);
    uint8_t* counters = g_getComponent(entity, nullptr, &kCounters);
    uint8_t* stamina = counters != nullptr ? Field<uint8_t*>(counters, kStamina) : nullptr;
    const float most = stamina != nullptr ? Field<float>(stamina, kStaminaMost) : 0.0f;
    return most > 0.0f ? Field<float>(stamina, kStaminaLeft) / most : 1.0f;
}
