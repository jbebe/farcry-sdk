#include "engine/damage.h"

#include "engine/entity.h"
#include "engine/memory.h"
#include "engine/player.h"

namespace {
    using namespace DevTools::Damage;
    using DevTools::Read;

    // CEntityEventStims; its instigator is an entity holder.
    constexpr ptrdiff_t kStimInstigator = 0x40;
    constexpr ptrdiff_t kStimType = 0x48;
    constexpr ptrdiff_t kStimLevel = 0x58;
    constexpr ptrdiff_t kStimBodyParts = 0x5C;

    // In the order CFCXCountersService::ApplyLocationMultiplier tests the bits.
    constexpr struct {
        uint32_t mask;
        BodyPart part;
    } kPartBits[] = {{0x003, kTorso}, {0x040, kHead}, {0x028, kHands},
                     {0x500, kFeet},  {0x014, kArms}, {0x280, kLegs}};
}

namespace DevTools::Damage {

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

void* Instigator(const uint8_t* stim) { return Entity::Of(Read<void*>(stim, kStimInstigator)); }

bool ByPlayer(const uint8_t* stim) {
    void* instigator = Instigator(stim);
    return instigator != nullptr && instigator == Player::Entity();
}

}
