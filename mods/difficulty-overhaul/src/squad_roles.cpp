// Squad roles: half of a squad assaults and half suppresses, firing from cover.
#include "engine/memory.h"
#include "fcse_api.h"

#include <algorithm>

using namespace DifficultyOverhaul;

namespace {
    // __thiscall with three stack arguments, which a free function spells __fastcall.
    using FindRoleDistributionFn = void(__fastcall*)(void* lieutenant, void* unused, int* assault, int* suppress,
                                                     const int* members);

    // CDispatcherSquadLieutenant::FindRoleDistribution.
    FCSE::Relocation<FindRoleDistributionFn> g_findRoleDistribution{FCSE::Uplay(0x00934E10)};
    FindRoleDistributionFn g_originalFindRoleDistribution = nullptr;

    // Hardcore: the share of a squad that assaults; the game's is 0.7, 0.9 on Infamous, one soldier on Easy.
    constexpr float kAssaultShare = 0.5f;

    // The game's own rounding: a lone soldier assaults, a larger squad's assaulters round down.
    void __fastcall FindRoleDistributionDetour(void*, void*, int* assault, int* suppress, const int* members) {
        *assault = *members < 2 ? 1 : static_cast<int>(static_cast<float>(*members) * kAssaultShare);
        *suppress = std::max(*members - *assault, 0);
    }
}

void InstallSquadRoles() {
    if (!Hook(g_findRoleDistribution, &FindRoleDistributionDetour, &g_originalFindRoleDistribution)) {
        FCSE::Logf("squad roles: the lieutenant's role split was not found in this build");
    }
}
