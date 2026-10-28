// Damage stims and the counters components they hit: the fields and handlers every damage feature
// reads.
#include "engine/damage.h"

#include "engine/game.h"
#include "engine/memory.h"

#include <cmath>

namespace {
    using namespace DifficultyOverhaul::Damage;

    // CEntityEventStims.
    constexpr ptrdiff_t kStimSender = 0x3C;
    constexpr ptrdiff_t kStimInstigator = 0x40;
    constexpr ptrdiff_t kStimType = 0x48;
    constexpr ptrdiff_t kStimLevel = 0x58;
    constexpr ptrdiff_t kStimBodyParts = 0x5C;

    // CFCXCountersComponent, its health CCounter, and CFCXCountersService.
    constexpr ptrdiff_t kCountersHealth = 0x44;
    constexpr ptrdiff_t kCountersService = 0x58;
    constexpr ptrdiff_t kCounterValue = 0x10;
    constexpr ptrdiff_t kCounterMax = 0x14;
    constexpr ptrdiff_t kServicePartMultipliers = 0xE8;

    // CCounter::SetCurrentValue, and the reason the game gives its own health changes.
    using SetCounterValueFn = void(__fastcall*)(void* counter, void* unused, float value,
                                                uint64_t instigator, const uint32_t* reason);
    constexpr ptrdiff_t kSetCounterValueSlot = 0x10;
    constexpr uint32_t kHealthChangeReason = 0x59F2984F;

    // In the order CFCXCountersService::ApplyLocationMultiplier tests the bits.
    constexpr struct {
        uint32_t mask;
        BodyPart part;
    } kPartBits[] = {{0x003, kTorso}, {0x040, kHead}, {0x028, kHands},
                     {0x500, kFeet},  {0x014, kArms}, {0x280, kLegs}};
}

namespace DifficultyOverhaul::Damage {

FCSE::Relocation<StimHandlerFn> PlayerDamage{FCSE::Uplay(0x006A2880)};
FCSE::Relocation<StimHandlerFn> SoldierDamage{FCSE::Uplay(0x0069C9C0)};

int Type(const uint8_t* stim) { return Read<int>(stim, kStimType); }

int Level(const uint8_t* stim) { return Read<int>(stim, kStimLevel); }

BodyPart Part(const uint8_t* stim) {
    const auto parts = Read<uint32_t>(stim, kStimBodyParts);
    for (const auto& bit : kPartBits) {
        if (parts & bit.mask) {
            return bit.part;
        }
    }
    return kNoBodyPart;
}

void* Instigator(const uint8_t* stim) {
    return Game::ProxyEntity(Read<const void*>(stim, kStimInstigator));
}

bool ByPlayer(const uint8_t* stim) { return Game::IsPlayerEntity(Instigator(stim)); }

bool SentByPlayer(const uint8_t* stim) {
    return Game::IsPlayerEntity(Game::ProxyEntity(Read<const void*>(stim, kStimSender)));
}

namespace {
    float HealthCounterField(const void* counters, ptrdiff_t field) {
        const auto* counter = Read<const void*>(counters, kCountersHealth);
        return counter != nullptr ? Read<float>(counter, field) : NAN;
    }
}

float Health(const void* counters) { return HealthCounterField(counters, kCounterValue); }

float MaxHealth(const void* counters) { return HealthCounterField(counters, kCounterMax); }

void SetHealth(void* counters, float health) {
    auto* counter = Read<void*>(counters, kCountersHealth);
    if (counter != nullptr) {
        Read<SetCounterValueFn>(Read<const void*>(counter, 0), kSetCounterValueSlot)(
            counter, nullptr, health, ~uint64_t{0}, &kHealthChangeReason);
    }
}

void* Owner(void* counters) { return FCSE::ApiPointer()->EntityData->EntityOf(counters); }

float* PartMultipliers(const void* counters) {
    auto* service = Read<uint8_t*>(counters, kCountersService);
    return service != nullptr ? reinterpret_cast<float*>(service + kServicePartMultipliers) : nullptr;
}

}
