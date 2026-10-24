// The player's pawn. See docs/docs/engine-internals/first-person-aiming.md#holding-the-breath.
#include "engine/pawn.h"

#include "fcse_api.h"

#include <algorithm>
#include <cmath>

namespace {
    using AimingOverhaul::Field;

    // Both __thiscall, which a free function spells __fastcall with an unused EDX.
    using LockFn = void(__fastcall*)(void* entity, void* unused);
    using GetComponentFn = uint8_t*(__fastcall*)(void* entity, void* unused, const uint32_t* id);
    FCSE::Relocation<LockFn> g_lock{FCSE::Uplay(0x004DD4F0)};
    FCSE::Relocation<GetComponentFn> g_getComponent{FCSE::Uplay(0x004E2CC0)};

    constexpr uint32_t kCounters = FCSE::Crc32("CFCXCountersComponentPlayer");
    constexpr uint32_t kShaker = FCSE::Crc32("CCameraShakeAndPadRumbleComponent");

    // A pawn's entity proxy and the proxy's entity; the counters' health and stamina, each a
    // counter of what is left and the most there can be; and the camera shake's three angles.
    constexpr ptrdiff_t kPawnProxy = 0x08;
    constexpr ptrdiff_t kProxyEntity = 0x0C;
    constexpr ptrdiff_t kHealth = 0x44;
    constexpr ptrdiff_t kStamina = 0x64;
    constexpr ptrdiff_t kCounterLeft = 0x10;
    constexpr ptrdiff_t kCounterMost = 0x14;
    constexpr ptrdiff_t kShake = 0x128;
    // An entity's place in the world, the last row of its 4x4.
    constexpr ptrdiff_t kEntityPlace = 0x60;

    // The pawn's entity, or null.
    uint8_t* Entity(uint8_t* pawn) {
        uint8_t* proxy = Field<uint8_t*>(pawn, kPawnProxy);
        return proxy != nullptr ? Field<uint8_t*>(proxy, kProxyEntity) : nullptr;
    }

    // The pawn entity's component of the class named by `id`, or null.
    uint8_t* Component(uint8_t* pawn, const uint32_t& id) {
        uint8_t* entity = g_lock && g_getComponent ? Entity(pawn) : nullptr;
        if (entity == nullptr) {
            return nullptr;
        }
        g_lock(entity, nullptr);
        return g_getComponent(entity, nullptr, &id);
    }

    // The share left of the counter at `offset` on the pawn's counters, one where there is none.
    float Left(uint8_t* pawn, ptrdiff_t offset) {
        uint8_t* counters = Component(pawn, kCounters);
        uint8_t* counter = counters != nullptr ? Field<uint8_t*>(counters, offset) : nullptr;
        const float most = counter != nullptr ? Field<float>(counter, kCounterMost) : 0.0f;
        return most > 0.0f ? Field<float>(counter, kCounterLeft) / most : 1.0f;
    }
}

float AimingOverhaul::Pawn::Stamina(uint8_t* pawn) {
    return Left(pawn, kStamina);
}

float AimingOverhaul::Pawn::Health(uint8_t* pawn) {
    return Left(pawn, kHealth);
}

bool AimingOverhaul::Pawn::Position(uint8_t* pawn, float out[3]) {
    uint8_t* entity = Entity(pawn);
    if (entity == nullptr) {
        return false;
    }
    const float* place = &Field<float>(entity, kEntityPlace);
    std::copy_n(place, 3, out);
    return true;
}

float AimingOverhaul::Pawn::Shake(uint8_t* pawn) {
    uint8_t* shaker = Component(pawn, kShaker);
    if (shaker == nullptr) {
        return 0.0f;
    }
    const float* angles = &Field<float>(shaker, kShake);
    return std::sqrt(angles[0] * angles[0] + angles[1] * angles[1] + angles[2] * angles[2]);
}
