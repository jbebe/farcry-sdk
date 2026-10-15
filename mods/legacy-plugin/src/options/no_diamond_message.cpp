// Hide diamond message: a diamond pickup no longer queues the engine's own "diamonds" HUD message.
//
// From Scubrah's Patch, legacy/scubrahs-patch/features/economy-briefcase-tracker.md (its Dunia.dll
// half; the scripted message that replaces it is data). PushDiamondPicked's `jmp +7` to the queue
// call (Steam 0x10686DBB, GOG 0x106792AB) becomes `jmp +16h`, past it. The unqueued message leaks.
// Scubrah's `jmp +2Ah` also skips freeing a local string, and its second patch is a no-op. Live.
#include "fcse_api.h"

#include <cstdint>

namespace {
    constexpr size_t kQueueJump = 17;
    constexpr uint8_t kToQueue = 0x07;
    constexpr uint8_t kPastQueue = 0x16;

    uint8_t* g_site = nullptr;
}

void InstallNoDiamondMessage() {
    FCSE::Relocation<uint8_t*> push{
        FCSE::Pattern("6A 13 8B C8 E8 ?? ?? ?? ?? 89 84 24 84 00 00 00 EB 07 89 9C 24 84 00 00 00 "
                      "8D 84 24 84 00 00 00 50 8B CE E8")};
    if (!push) {
        FCSE::Logf("hide diamond message: the diamond message's queue call was not found - left as "
                   "shipped");
        return;
    }

    g_site = push.get() + kQueueJump;
}

void __cdecl OnNoDiamondMessageChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    if (g_site == nullptr) {
        return;
    }

    const bool on = value->asCheckbox;
    if (FCSE::ApiPointer()->Patch(g_site, on ? &kPastQueue : &kToQueue, 1)) {
        FCSE::Logf("hide diamond message: %s", on ? "on" : "off");
    }
}
