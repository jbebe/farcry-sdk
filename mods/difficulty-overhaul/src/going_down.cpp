// Going down: a soldier the player's limb hit would kill goes down wounded instead, every time, where
// the game rolls against the player's reputation and caps how many soldiers are down at once.
#include "engine/damage.h"
#include "engine/memory.h"
#include "fcse_api.h"

#include <cfloat>
#include <cstdint>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with two stack arguments and with one, which free functions spell __fastcall.
    using CanGoDownFn = bool(__fastcall*)(void* counters, void* unused, const uint8_t* stim,
                                          float chanceModifier);
    using MaxDownFn = uint32_t(__fastcall*)(void* service, void* unused, int reputationLevel);

    // CFCXCountersComponentAI::CanHealthFailureOccur and CFCXCountersService::GetMaxNPCsInFailure.
    FCSE::Relocation<CanGoDownFn> g_canGoDown{FCSE::Uplay(0x0069B8D0)};
    FCSE::Relocation<MaxDownFn> g_maxDown{FCSE::Uplay(0x007645C0)};
    CanGoDownFn g_originalCanGoDown = nullptr;
    MaxDownFn g_originalMaxDown = nullptr;

    // Set while the game decides on a limb hit by the player: its cap is lifted and its roll won.
    thread_local bool t_limbHit = false;

    bool LimbHitByPlayer(const uint8_t* stim) {
        const Damage::BodyPart part = Damage::Part(stim);
        return part >= Damage::kArms && part <= Damage::kFeet && Damage::ByPlayer(stim);
    }

    bool __fastcall CanGoDownDetour(void* counters, void* unused, const uint8_t* stim,
                                    float chanceModifier) {
        if (!LimbHitByPlayer(stim)) {
            return g_originalCanGoDown(counters, unused, stim, chanceModifier);
        }
        t_limbHit = true;
        const bool down = g_originalCanGoDown(counters, unused, stim, FLT_MAX);
        t_limbHit = false;
        return down;
    }

    uint32_t __fastcall MaxDownDetour(void* service, void* unused, int reputationLevel) {
        return t_limbHit ? UINT16_MAX : g_originalMaxDown(service, unused, reputationLevel);
    }
}

void InstallGoingDown() {
    if (!Hook(g_canGoDown, &CanGoDownDetour, &g_originalCanGoDown) ||
        !Hook(g_maxDown, &MaxDownDetour, &g_originalMaxDown)) {
        FCSE::Logf("going down: the soldiers' wounded state was not found in this build");
    }
}
