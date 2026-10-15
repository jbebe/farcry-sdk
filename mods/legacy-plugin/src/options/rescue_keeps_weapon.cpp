// Keep weapon on rescue: a buddy rescue no longer swaps the player's secondary weapon for a pistol.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/buddy-rescue-fluid.md (its Dunia.dll half;
// the short rescue is data). CGOStateEventRescue::Activate's jump table sends request type 11, "try
// drop weapons", to TryDropWeapon. Its entry (Steam 0x10729360, GOG 0x1071BE50) is pointed at the
// switch's own out-of-range exit, which returns doing nothing. Scubrah points it at the case-13
// handler instead, which only happens to do nothing for the player. Live.
#include "fcse_api.h"

#include <cstdint>

namespace {
    constexpr size_t kOutOfRangeJump = 14;
    constexpr size_t kJumpTable = 21;
    // Request type 11, less the 1 the switch subtracts.
    constexpr size_t kTryDropWeapons = 10;

    uintptr_t* g_entry = nullptr;
    uintptr_t g_shipped = 0;
    uintptr_t g_doNothing = 0;
}

void InstallRescueKeepsWeapon() {
    FCSE::Relocation<uint8_t*> dispatch{
        FCSE::Pattern("8B 53 10 8B 48 40 83 C2 FF 83 FA 14 0F 87 ?? ?? ?? ?? "
                      "FF 24 95 ?? ?? ?? ??")};
    if (!dispatch) {
        FCSE::Logf("keep weapon on rescue: the rescue event's switch was not found - left as "
                   "shipped");
        return;
    }

    uint8_t* const at = dispatch.get();
    const int32_t outOfRange = *reinterpret_cast<const int32_t*>(at + kOutOfRangeJump);
    g_doNothing = reinterpret_cast<uintptr_t>(at + kOutOfRangeJump + sizeof(int32_t)) + outOfRange;
    g_entry = *reinterpret_cast<uintptr_t**>(at + kJumpTable) + kTryDropWeapons;
    g_shipped = *g_entry;
}

void __cdecl OnRescueKeepsWeaponChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    if (g_entry == nullptr) {
        return;
    }

    const bool on = value->asCheckbox;
    if (FCSE::ApiPointer()->Patch(g_entry, on ? &g_doNothing : &g_shipped, sizeof(uintptr_t))) {
        FCSE::Logf("keep weapon on rescue: %s", on ? "on" : "off");
    }
}
