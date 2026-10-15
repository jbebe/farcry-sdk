// Briefings keep control: an NPC's briefing no longer stands the player up and takes control.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/skippable-dialog.md (its Dunia.dll half).
// A CPawnAgent event handler's briefing branch lowers the weapon, then stands the player up, puts
// the player in control mode 2 and turns the NPC to face them before sending "briefing". The
// stand-up call (Steam 0x1097485E, GOG 0x10963ACE) becomes `jmp +5Dh` to that send. Live.
#include "fcse_api.h"

#include <cstdint>
#include <cstring>

namespace {
    constexpr size_t kStandUpCall = 16;
    constexpr uint8_t kJumpToSend[] = {0xEB, 0x5D, 0x90, 0x90, 0x90};

    uint8_t* g_site = nullptr;
    uint8_t g_shipped[sizeof(kJumpToSend)];
}

void InstallBriefingControl() {
    FCSE::Relocation<uint8_t*> branch{
        FCSE::Pattern("8B CF E8 ?? ?? ?? ?? 8B C8 E8 ?? ?? ?? ?? 8B CF E8 ?? ?? ?? ?? "
                      "8B CF E8 ?? ?? ?? ?? 8B CF C7 80 A8 00 00 00 02 00 00 00 "
                      "E8 ?? ?? ?? ?? C7 80 A4 00 00 00 00 00 00 00")};
    if (!branch) {
        FCSE::Logf("briefings keep control: the briefing's stand-up call was not found - left as "
                   "shipped");
        return;
    }

    g_site = branch.get() + kStandUpCall;
    std::memcpy(g_shipped, g_site, sizeof(g_shipped));
}

void __cdecl OnBriefingControlChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    if (g_site == nullptr) {
        return;
    }

    const bool on = value->asCheckbox;
    if (FCSE::ApiPointer()->Patch(g_site, on ? kJumpToSend : g_shipped, sizeof(g_shipped))) {
        FCSE::Logf("briefings keep control: %s", on ? "on" : "off");
    }
}
