// Bonus content: the predecessor tapes and the two extra machetes, gated behind services that no
// longer answer.
//
// Both are ordinary content shipped inside the game's own files - seven Intel Bonus predecessor
// missions, and the Primitive and Homemade machete variants - held behind an ownership check that
// was satisfied by redeeming a Ubisoft promotion. The promotion ended, the service behind it was
// retired, and the content became permanently unreachable in every copy of the game. Nothing here
// bypasses a purchase; the check has no correct answer left to give.
//
// The two builds gate them differently, which is why the predecessor unlock needs a pattern per
// build rather than one address:
//
//   Steam / Ubisoft Connect   IsBonusUnlocked(index) -> PrivilegesClient::GetPrivilege
//                             (Steam Dunia.dll 0x102E1D10)
//   GOG / patched retail      IsBonusUnlocked(index) -> RegQueryValueEx on
//                             HKCU\Software\Ubisoft\Far Cry 2, value "PartnerKey%d"
//                             (GOG Dunia.dll 0x10048900)
//
// The machetes gate is the same registry mechanism on both builds - value "MachetesKey" - and its
// address library entry is exact, so it needs no pattern:
//
//   IsMachetesUnlocked()      Steam Dunia.dll 0x100488D0, GOG Dunia.dll 0x100489A0
//
// Both are patched at the prologue, so the original body never runs at all - which is also why the
// registry gates leak no key handle: the key is never opened.
//
// Unlocking the machetes makes the game's own Options - Game - Machete Type row appear, a three-way
// selector with localized labels (SETTING_MACHETE, MBOXLISTMACHETE_TYPE_1..3) that stores the
// chosen type in the player's profile at options+0x11C. UFCP deliberately does not duplicate that
// row: the game's own is localized, persists properly, and is where a player would look for it.
//
// One caveat found while reading CFCXOptionGamePage::RefreshOptionList: the machete row is gated on
// the unlock check AND on the current world id being 0x83AE70EF, so it appears in the singleplayer
// campaign context only. That is the stock behaviour and is not something this patch changes.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // mov al, 1 / ret 4. Both bonus gates are a bool taking one stack argument, so one body serves
    // whichever build is running.
    constexpr uint8_t kReturnTrueRet4[] = {0xB0, 0x01, 0xC2, 0x04, 0x00};

    // mov eax, 1 / ret. The machetes gate takes no arguments and returns in eax.
    constexpr uint8_t kReturnTrueRet[] = {0xB8, 0x01, 0x00, 0x00, 0x00, 0xC3};

    // The privileges-based gate, matched through the call to GetPrivilege - the call's displacement
    // is wildcarded because it is the one part that moves. Present in the Steam build only.
    FCSE::Relocation<uint8_t*> g_bonusPrivileges{FCSE::Pattern(
        "8B 49 0C 85 C9 74 16 8B 44 24 04 50 E8 ?? ?? ?? ?? 84 C0 74 08 B8 01 00 00 00 C2 04 00")};

    // The registry-based gate, present in the GOG build only. The pushed string pointer and the
    // import slot are wildcarded; the 0x110-byte frame in the first instruction is what separates
    // this from the otherwise identical machetes gate, which reserves 0x10.
    FCSE::Relocation<uint8_t*> g_bonusRegistry{FCSE::Pattern(
        "81 EC 10 01 00 00 53 8D 44 24 08 50 68 19 00 02 00 33 DB 53 68 ?? ?? ?? ?? 68 01 00 00 "
        "80 FF 15 ?? ?? ?? ??")};

    // Named from the Steam build; the address library resolves it on either.
    FCSE::Relocation<uint8_t*> g_machetes{FCSE::Uplay(0x000488D0)};
}

void ApplyPredecessorTapesUnlock() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    // Exactly one of the two implementations exists in any given build, so this is a choice between
    // them rather than a fallback: the other one's code is genuinely not there to find.
    uint8_t* gate = nullptr;
    if (g_bonusPrivileges) {
        gate = g_bonusPrivileges.get();
    } else if (g_bonusRegistry) {
        gate = g_bonusRegistry.get();
    }

    if (gate == nullptr) {
        api->Log("bonus content: neither ownership check was found in this build - predecessor "
                 "tapes not unlocked");
        return;
    }

    if (api->Patch(gate, kReturnTrueRet4, sizeof(kReturnTrueRet4))) {
        api->Log("predecessor tapes unlocked");
    }
}

void ApplyMachetesUnlock() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_machetes) {
        api->Log("bonus content: the machetes ownership check is not mapped on this build - "
                 "machetes not unlocked");
        return;
    }

    if (api->Patch(g_machetes.get(), kReturnTrueRet, sizeof(kReturnTrueRet))) {
        api->Log("machetes unlocked - pick a type in Options, Game, Machete Type");
    }
}
