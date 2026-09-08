// Infinite ammo.
//
// `cheat_UnlimitedAmmo` sets one profile flag, re-applied every frame here because a profile reload
// puts the file's value back.
//
// That flag has a side effect the command does not mention. Magazines and consumables come out of
// one shared item decrementer, which returns without decrementing anything while the flag is set -
// so unlimited ammo silently makes syringes free too, and healing stops costing anything. That is
// not what the option says it does, and it quietly removes the malaria and health economy the
// campaign is built around.
//
// So the syringe is put back. The decrementer is hooked, the three call sites that spend a syringe
// are identified by which of them called - there is nothing in the arguments that distinguishes a
// syringe from a magazine - and the flag is cleared for that one call and put straight back.
//
// This corrects the `cheat_UnlimitedAmmo` catalog row too, which reaches the same flag by a
// different road: the hook reads the live profile field rather than this option's setting.
#include "engine/game_profile.h"
#include "fcse_api.h"

#include <cstdint>
#include <intrin.h>

namespace {
    using ItemConsumeFn = int32_t(__fastcall*)(void* pool, void* unused, int32_t amount);

    FCSE::Relocation<ItemConsumeFn> g_itemConsume{FCSE::Uplay(0x00145100)};
    ItemConsumeFn g_originalItemConsume = nullptr;

    // The three sites that spend a syringe. All three end on the same thirteen bytes - the CALL and
    // the argument setup before it - so each pattern carries the instructions ahead of that to be
    // unique; a pattern matching all three would be ambiguous and resolve to nothing.
    struct ConsumeSite {
        FCSE::Relocation<uint8_t*> call;
        size_t returnOffset;
    };

    ConsumeSite g_syringeSites[] = {
        {FCSE::Relocation<uint8_t*>{FCSE::Pattern("8B 44 24 18 8B 50 04 8B 00 8D 0C 90 8B 44 24 10 "
                                                  "3B C1 75 04 33 C9 EB 02 8B 08 6A 01 E8")},
         0x21},
        {FCSE::Relocation<uint8_t*>{FCSE::Pattern("8B 44 24 1C 8B 50 04 8B 00 8D 0C 90 8B 44 24 14 "
                                                  "3B C1 75 04 33 C9 EB 02 8B 08 6A 01 E8")},
         0x21},
        {FCSE::Relocation<uint8_t*>{FCSE::Pattern("8B 48 04 8B 10 8B 44 24 10 8D 0C 8A "
                                                  "3B C1 75 04 33 C9 EB 02 8B 08 6A 01 E8")},
         0x1D},
    };

    constexpr size_t kSyringeSiteCount = sizeof(g_syringeSites) / sizeof(g_syringeSites[0]);
    uintptr_t g_syringeReturn[kSyringeSiteCount]{};
    bool g_enabled = false;
    size_t g_syringeReturnCount = 0;

    // Nothing in the arguments says which item is being spent, so the caller is the only way to tell
    // a syringe from a magazine. _ReturnAddress must be read before anything the optimiser could
    // reorder ahead of it, which is also why this may not be inlined into the trampoline's caller.
    __declspec(noinline) int32_t __fastcall ItemConsumeDetour(void* pool, void* unused,
                                                              int32_t amount) {
        const auto caller = reinterpret_cast<uintptr_t>(_ReturnAddress());

        bool syringe = false;
        for (size_t i = 0; i < g_syringeReturnCount; ++i) {
            if (caller == g_syringeReturn[i]) {
                syringe = true;
                break;
            }
        }

        int32_t* unlimited =
            syringe ? DevTools::GameProfile::Field(DevTools::GameProfile::Cheat::UnlimitedAmmo)
                    : nullptr;
        if (unlimited == nullptr) {
            return g_originalItemConsume(pool, unused, amount);
        }

        const int32_t saved = *unlimited;
        *unlimited = 0;
        const int32_t result = g_originalItemConsume(pool, unused, amount);
        *unlimited = saved;

        return result;
    }
}

void InstallInfiniteAmmoHooks() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    for (ConsumeSite& site : g_syringeSites) {
        if (site.call) {
            g_syringeReturn[g_syringeReturnCount++] = site.call.address() + site.returnOffset;
        }
    }

    if (!g_itemConsume || g_syringeReturnCount != kSyringeSiteCount) {
        api->Log("infinite ammo: not every syringe call site was found in this build - unlimited "
                 "ammo will make healing free as well");
        return;
    }

    api->Hook(reinterpret_cast<void*>(g_itemConsume.address()),
              reinterpret_cast<void*>(&ItemConsumeDetour),
              reinterpret_cast<void**>(&g_originalItemConsume));
}

int GetInfiniteAmmo() { return g_enabled ? 1 : 0; }

void SetInfiniteAmmo(int value) {
    if ((value != 0) == g_enabled) {
        return;
    }

    g_enabled = value != 0;
    DevTools::GameProfile::Want(DevTools::GameProfile::Cheat::UnlimitedAmmo, g_enabled);

    FCSE::ApiPointer()->Log(g_enabled ? "infinite ammo: on - syringes still cost a syringe"
                                      : "infinite ammo: off");
}
