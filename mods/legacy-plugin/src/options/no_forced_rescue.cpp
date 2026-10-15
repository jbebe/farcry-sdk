// No forced buddy rescues: factions stop assigning buddy rescue missions, so a bugged one cannot
// block the story.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/buddy-rescue-optional.md. Two property lists
// lose one property each, by lengthening the `jmp +2` before its registration: CBuddyMission's
// Active (Steam 0x1074D9CF, GOG 0x107404BF, to `jmp +4Eh`) and the mission manager's
// MissionsSinceBuddyUnlock (Steam 0x10756C05, GOG 0x10749665, to `jmp +4Ah`). Each list is built
// once, when first asked for, so this applies at launch and a change takes effect next launch. It
// changes what a save holds, so the row asks for a new game.
#include "fcse_api.h"

#include <cstdint>

namespace {
    constexpr size_t kActiveJump = 24;
    constexpr uint8_t kPastActive = 0x4E;
    constexpr size_t kSinceUnlockJump = 20;
    constexpr uint8_t kPastSinceUnlock = 0x4A;

    uint8_t* g_activeJump = nullptr;
    uint8_t* g_sinceUnlockJump = nullptr;
    bool g_launchValueSeen = false;
}

void InstallNoForcedRescue() {
    FCSE::Relocation<uint8_t*> active{FCSE::Pattern(
        "C7 06 ?? ?? ?? ?? 89 5E 0C C7 46 10 ?? ?? ?? ?? C7 46 14 ?? ?? ?? ?? EB 02 33 F6 56 "
        "68 ?? ?? ?? ?? E8 ?? ?? ?? ?? 53 6A 14 E8 ?? ?? ?? ?? 8B F0 83 C4 10 3B F3 74 2E 53 53 C7 06 ?? ?? ?? ?? "
        "68 ?? ?? ?? ?? 8D 4E 08 C7 46 04 ?? ?? ?? ?? E8 ?? ?? ?? ?? C7 06 ?? ?? ?? ?? "
        "C7 46 0C 4C 00 00 00 89 5E 10")};
    FCSE::Relocation<uint8_t*> sinceUnlock{FCSE::Pattern(
        "C7 06 ?? ?? ?? ?? 89 6E 0C C7 46 10 01 00 00 00 89 5E 14 EB 02 33 F6 56 57 E8 ?? ?? ?? ?? "
        "53 6A 14 E8 ?? ?? ?? ?? 8B F0 83 C4 10 3B F3 74 2E 53 53 C7 06 ?? ?? ?? ?? 68 ?? ?? ?? ?? "
        "8D 4E 08 C7 46 04 ?? ?? ?? ?? E8 ?? ?? ?? ?? C7 06 ?? ?? ?? ?? C7 46 0C 20 01 00 00")};
    if (!active || !sinceUnlock) {
        FCSE::Logf("no forced buddy rescues: the buddy mission properties were not found - left as "
                   "shipped");
        return;
    }

    g_activeJump = active.get() + kActiveJump;
    g_sinceUnlockJump = sinceUnlock.get() + kSinceUnlockJump;
}

void __cdecl OnNoForcedRescueChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    if (g_activeJump == nullptr) {
        return;
    }

    const bool on = value->asCheckbox;
    if (g_launchValueSeen) {
        FCSE::Logf("no forced buddy rescues: %s from next launch", on ? "on" : "off");
        return;
    }
    g_launchValueSeen = true;
    if (!on) {
        return;
    }

    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    if (api->Patch(g_activeJump, &kPastActive, 1) &&
        api->Patch(g_sinceUnlockJump, &kPastSinceUnlock, 1)) {
        FCSE::Logf("no forced buddy rescues: on");
    }
}
