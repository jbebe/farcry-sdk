// Going down: a soldier the player's leg or foot hit would kill goes down wounded instead, every time,
// where the game rolls against the player's reputation and caps how many soldiers are down at once; a
// hit anywhere else never downs anyone, and limb hits on a downed soldier do less than the game's share.
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

    // CFCXCountersComponentAI's fHealthFailureLimbsHitModifier: the share of a limb hit a downed soldier
    // takes, the game's 0.2.
    constexpr ptrdiff_t kDownedLimbShare = 0x94;
    constexpr float kDownedLimbShareValue = 0.11f;

    // Set while the game decides on a leg or foot hit by the player: its cap is lifted and its roll won.
    thread_local bool t_legHit = false;

    bool __fastcall CanGoDownDetour(void* counters, void* unused, const uint8_t* stim,
                                    float chanceModifier) {
        const Damage::BodyPart part = Damage::Part(stim);
        if (part != Damage::kLegs && part != Damage::kFeet && part != Damage::kNoBodyPart) {
            return false;
        }
        t_legHit = part != Damage::kNoBodyPart && Damage::ByPlayer(stim);
        const bool down = g_originalCanGoDown(counters, unused, stim, t_legHit ? FLT_MAX : chanceModifier);
        t_legHit = false;
        if (down) {
            Write<float>(counters, kDownedLimbShare, kDownedLimbShareValue);
        }
        return down;
    }

    uint32_t __fastcall MaxDownDetour(void* service, void* unused, int reputationLevel) {
        return t_legHit ? UINT16_MAX : g_originalMaxDown(service, unused, reputationLevel);
    }
}

void InstallGoingDown() {
    if (!Hook(g_canGoDown, &CanGoDownDetour, &g_originalCanGoDown) ||
        !Hook(g_maxDown, &MaxDownDetour, &g_originalMaxDown)) {
        FCSE::Logf("going down: the soldiers' wounded state was not found in this build");
    }
}
