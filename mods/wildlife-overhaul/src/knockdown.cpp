// A knockdown stim floors the player: knockout.lua's Faint when they survive it, Die when not.
#include "knockdown.h"

#include "engine/lua.h"
#include "fcse_api.h"

#include <atomic>
#include <cstdint>
#include <utility>
#include <windows.h>

namespace {
    // __thiscall with one stack argument, which a free function spells __fastcall.
    using HandleDamageStimFn = void(__fastcall*)(void* counters, void* unused, const uint8_t* stim);
    using SetHealthFn = void(__fastcall*)(void* counters, void* unused, float health);
    using UpdateFn = void(__fastcall*)(void* game, void* unused);

    // CFCXCountersComponentPlayer::HandleDamageStim and SetHealth, and CXGame::Update.
    FCSE::Relocation<HandleDamageStimFn> g_handleDamageStim{FCSE::Uplay(0x006A2880)};
    FCSE::Relocation<SetHealthFn> g_setHealth{FCSE::Pattern(
        "51 8B 49 44 D9 44 24 08 56 57 8D 7C 24 08 57 83 CA FF 83 C8 FF 52 50 C7 44 24 14 4F 98 F2 59")};
    FCSE::Relocation<UpdateFn> g_update{FCSE::Uplay(0x0065AEA0)};

    HandleDamageStimFn g_originalHandleDamageStim = nullptr;
    SetHealthFn g_originalSetHealth = nullptr;
    UpdateFn g_originalUpdate = nullptr;

    // CEntityEventStims::sDetail, a CStringID: the CRC-32 of the brain task's `detail`.
    constexpr ptrdiff_t kStimDetail = 0x44;
    constexpr uint32_t kKnockdownDetail = FCSE::Crc32("wildlife_knockdown");

    // The health a lethal knockdown stim leaves, so that Die chooses the ending.
    constexpr float kLastBreath = 1.0f;
    // Longest a knockdown blocks the next one.
    constexpr ULONGLONG kMaxDownMs = 30000;

    enum Ending { kNone, kFaint, kDie };

    // The knockdown stim being applied on this thread, and what its health write chose.
    struct Gore {
        void* counters;
        Ending ending;
        float health;
    };
    thread_local Gore t_gore{};

    std::string g_script;
    std::atomic<Ending> g_ending{kNone};
    // When the player went down, or 0 while they are up.
    std::atomic<ULONGLONG> g_downSince{0};
    void* g_counters = nullptr;
    float g_health = 0.0f;

    bool IsDown() {
        const ULONGLONG since = g_downSince;
        return since != 0 && GetTickCount64() - since < kMaxDownMs;
    }

    // Called by knockout.lua once the player is up: sets the health the stim left.
    int __cdecl Restore(void*) {
        if (IsDown()) {
            g_originalSetHealth(g_counters, nullptr, g_health);
        }
        g_downSince = 0;
        return 0;
    }

    void __fastcall SetHealthDetour(void* counters, void* unused, float health) {
        if (t_gore.counters == counters) {
            t_gore.ending = health <= 0.0f ? kDie : kFaint;
            t_gore.health = health = t_gore.ending == kDie ? kLastBreath : health;
        }
        g_originalSetHealth(counters, unused, health);
    }

    void __fastcall HandleDamageStimDetour(void* counters, void* unused, const uint8_t* stim) {
        if (*reinterpret_cast<const uint32_t*>(stim + kStimDetail) != kKnockdownDetail) {
            g_originalHandleDamageStim(counters, unused, stim);
            return;
        }
        // A knockdown stim does nothing while the player is down or getting up.
        if (IsDown()) {
            return;
        }

        t_gore = {counters, kNone, 0.0f};
        g_originalHandleDamageStim(counters, unused, stim);
        const Gore gore = std::exchange(t_gore, Gore{});
        if (gore.ending == kNone) {
            return;
        }

        g_counters = counters;
        g_health = gore.health;
        g_downSince = GetTickCount64();
        g_ending = gore.ending;
    }

    void __fastcall UpdateDetour(void* game, void* unused) {
        g_originalUpdate(game, unused);

        const Ending ending = g_ending.exchange(kNone);
        if (ending == kNone) {
            return;
        }
        const char* call = ending == kFaint ? "WildlifeKnockout:Faint()" : "WildlifeKnockout:Die()";
        FCSE::Logf("knockdown: %s at %.0f health", call, g_health);
        const bool ran = WildlifeOverhaul::Lua::Run(g_script + "\n" + call, "WildlifeKnockdownRestore",
                                                    &Restore);
        if (!ran) {
            FCSE::Logf("knockdown: knockout.lua did not run");
        }
        // Only a faint has a get-up to wait for; a dead player takes no damage.
        if (!ran || ending == kDie) {
            g_downSince = 0;
        }
    }

    template <typename Fn>
    bool HookSite(FCSE::Relocation<Fn>& site, Fn detour, Fn& original) {
        return FCSE::ApiPointer()->Hook(reinterpret_cast<void*>(site.address()),
                                        reinterpret_cast<void*>(detour),
                                        reinterpret_cast<void**>(&original));
    }
}

namespace WildlifeOverhaul::Knockdown {

bool Install(std::string script) {
    if (!g_handleDamageStim || !g_setHealth || !g_update || !WildlifeOverhaul::Lua::Found()) {
        FCSE::Logf("knockdown: the player's damage handling or the script system was not found in "
                   "this build");
        return false;
    }
    g_script = std::move(script);
    return HookSite(g_setHealth, &SetHealthDetour, g_originalSetHealth) &&
           HookSite(g_handleDamageStim, &HandleDamageStimDetour, g_originalHandleDamageStim) &&
           HookSite(g_update, &UpdateDetour, g_originalUpdate);
}

}
