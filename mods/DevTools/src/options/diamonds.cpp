// Diamonds.
//
// `Cheat_AddDiamonds` adds a number to the wallet and that is the end of it: the diamonds are real
// from then on, they go into the next save, and a test run funded that way has quietly rewritten the
// progress of the profile it ran on. For anyone testing a bazaar, a mission payout or a weapon
// unlock, that is the wrong trade.
//
// So this is a target rather than a gift. The wallet is topped back up to the setting every frame,
// and the shortfall it took to get there is remembered as a grant, which makes real progress always
// the live count minus the grant. Three things keep the grant from ever becoming real:
//
//   - spending comes off the grant first, so buying a gun with granted money leaves real progress
//     exactly where it was;
//   - lowering the setting hands back only the part of the grant above the new target;
//   - a save writes the count with the grant subtracted out, and puts it straight back afterwards.
//
// Set it to zero and the grant is handed back, leaving the profile as it would have been. What this
// does not survive is the game being killed mid-session, which writes nothing and so loses nothing.
//
// See docs/docs/engine-internals/cheat-flags-and-economy.md.
#include "engine/pawn_tick.h"
#include "engine/player.h"
#include "fcse_api.h"

#include <cstdint>

namespace {
    constexpr ptrdiff_t kEconomyDiamondCount = 0x10;

    // Property descriptor: +0x04 name, +0x08 name hash, +0x0C field offset, +0x10 flags.
    constexpr ptrdiff_t kPropertyHash = 0x08;
    constexpr ptrdiff_t kPropertyOffset = 0x0C;

    // CRC-32 of the property name. The HUD mirrors carry the same hash on a different object, so
    // the field offset is part of the test.
    constexpr uint32_t kHashDiamondCount = 0x333DBF78;
    constexpr uint32_t kHashLastDiamondCount = 0x3A8909F7;
    constexpr int32_t kHudDiamondCount = 0x2BC;
    constexpr int32_t kHudLastDiamondCount = 0x2C8;

    constexpr size_t kEconomyCtorVTableSet = 0x1A;

    using SerialiseFn = void(__fastcall*)(void* descriptor, void* unused, void* object,
                                          void* visitor);

    // Four sightings, because no one of them covers every session: the component's constructor, the
    // two places a count changes, and the bazaar page snapshotting the wallet as it opens.
    FCSE::Relocation<uint8_t*> g_economyCtor{
        FCSE::Pattern("56 8B F1 E8 ?? ?? ?? ?? 33 C0 89 46 10 C7 06 ?? ?? ?? ?? C7 46 04 ?? ?? ?? "
                      "?? 89 46 14 89 46 18 89 46 1C 89 46 20 89 46 24 89 46 28 8B C6 5E C3")};
    FCSE::Relocation<uint8_t*> g_addDiamonds{FCSE::Pattern(
        "51 A1 ?? ?? ?? ?? A8 01 56 57 8B F9 75 12 83 C8 01 A3 ?? ?? ?? ?? C7 05 ?? ?? ?? ?? A8 64 "
        "B0 93")};
    FCSE::Relocation<uint8_t*> g_removeDiamonds{FCSE::Pattern("F6 05 ?? ?? ?? ?? 01 53 57 8B D9 75 11")};
    FCSE::Relocation<uint8_t*> g_bazaarSnapshot{
        FCSE::Pattern("8B 40 10 89 86 70 01 00 00 89 86 74 01 00 00 89 86 78 01 00 00")};

    // CConstIntProperty::Serialise, shared by every int32 property, which is why the descriptor is
    // filtered. Not an entry the address library knows, so it stays on a pattern.
    FCSE::Relocation<SerialiseFn> g_serialise{FCSE::Pattern(
        "56 8B C1 8B 70 0C 8B 4C 24 0C 8B 11 57 8B 7C 24 0C 8B 34 3E 83 C0 04 56 50 8B 82 A0 00 00 "
        "00 FF D0")};
    SerialiseFn g_originalSerialise = nullptr;

    int32_t g_target = 0;

    // The economy component and the vtable it was first seen with. It dies with the player while
    // this plugin's clock keeps ticking, hence the liveness test before every write.
    void* g_economy = nullptr;
    void* g_economyVTable = nullptr;

    // Granted diamonds still in the wallet.
    int32_t g_granted = 0;

    int32_t* LiveDiamonds() {
        return g_economy != nullptr
                   ? reinterpret_cast<int32_t*>(static_cast<uint8_t*>(g_economy) + kEconomyDiamondCount)
                   : nullptr;
    }

    // Hands the grant back; abandoning it would promote granted diamonds to real progress.
    void RetireGrant() {
        if (g_granted <= 0) {
            return;
        }

        if (DevTools::Player::ObjectIsLive(g_economy, g_economyVTable, DevTools::Player::Local())) {
            int32_t* live = LiveDiamonds();
            *live = (*live > g_granted) ? (*live - g_granted) : 0;
        }

        g_granted = 0;
    }

    // A different component is a different session. Settle the outgoing wallet before adopting it.
    void NoteEconomy(void* component) {
        if (component == nullptr || component == g_economy) {
            return;
        }

        RetireGrant();
        g_economy = component;
        g_economyVTable = *reinterpret_cast<void**>(component);
    }

    bool IsDiamondCountProperty(void* descriptor) {
        if (descriptor == nullptr) {
            return false;
        }

        auto* property = static_cast<uint8_t*>(descriptor);
        const uint32_t hash = *reinterpret_cast<uint32_t*>(property + kPropertyHash);
        const int32_t offset = *reinterpret_cast<int32_t*>(property + kPropertyOffset);

        if (hash == kHashDiamondCount) {
            return offset == kEconomyDiamondCount || offset == kHudDiamondCount;
        }
        if (hash == kHashLastDiamondCount) {
            return offset == kHudLastDiamondCount;
        }
        return false;
    }

    void OnTick(const DevTools::PawnTick::Frame& frame) {
        // Off with nothing outstanding is every frame for anyone not using this, so it comes before
        // the liveness test and the engine call inside it.
        if (g_target <= 0 && g_granted == 0) {
            return;
        }

        // The pointer is kept on failure: settling the grant later needs a live wallet.
        if (!DevTools::Player::ObjectIsLive(g_economy, g_economyVTable, frame.local)) {
            return;
        }

        int32_t* live = LiveDiamonds();

        if (g_target <= 0) {
            RetireGrant();
            return;
        }

        if (*live < g_target) {
            g_granted += g_target - *live;
            *live = g_target;
        } else if (g_granted > 0 && *live > g_target) {
            // The setting was lowered: hand back only the part of the grant above the new target.
            const int32_t over = *live - g_target;
            const int32_t back = (over < g_granted) ? over : g_granted;

            *live -= back;
            g_granted -= back;
        }

        // Trim the grant to what is still in the wallet, in case something outside lowered it.
        if (g_granted > *live) {
            g_granted = (*live > 0) ? *live : 0;
        }
    }

    void EconomyConstructed(FCSE_MidHookContext* ctx) {
        g_granted = 0;
        NoteEconomy(reinterpret_cast<void*>(ctx->esi));
    }

    void DiamondsAdded(FCSE_MidHookContext* ctx) { NoteEconomy(reinterpret_cast<void*>(ctx->ecx)); }

    void BazaarOpened(FCSE_MidHookContext* ctx) { NoteEconomy(reinterpret_cast<void*>(ctx->eax)); }

    // Before the engine's own clamp and store, so spending can be taken off the grant first.
    void DiamondsRemoved(FCSE_MidHookContext* ctx) {
        NoteEconomy(reinterpret_cast<void*>(ctx->ecx));

        int32_t* live = LiveDiamonds();
        if (g_granted <= 0 || live == nullptr) {
            return;
        }

        // The engine clamps a spend to the balance, so this has to as well.
        int32_t spent = *reinterpret_cast<int32_t*>(ctx->esp + 4);
        if (spent > *live) {
            spent = *live;
        }

        g_granted -= (spent < g_granted) ? spent : g_granted;
    }

    // The value reaches the visitor through a register rather than the field, so the substitution
    // goes around the call. Serialisation is single threaded, which is what makes that safe.
    void __fastcall SerialiseDetour(void* descriptor, void* unused, void* object, void* visitor) {
        if (g_granted <= 0 || object == nullptr || !IsDiamondCountProperty(descriptor)) {
            g_originalSerialise(descriptor, unused, object, visitor);
            return;
        }

        const int32_t offset =
            *reinterpret_cast<int32_t*>(static_cast<uint8_t*>(descriptor) + kPropertyOffset);
        auto* field = reinterpret_cast<int32_t*>(static_cast<uint8_t*>(object) + offset);

        const int32_t inflated = *field;
        *field = (inflated > g_granted) ? (inflated - g_granted) : 0;

        g_originalSerialise(descriptor, unused, object, visitor);

        *field = inflated;
    }
}

void InstallDiamondsHooks() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    DevTools::PawnTick::Subscribe(&OnTick);

    bool sighted = g_economyCtor && api->MidHook(g_economyCtor.get() + kEconomyCtorVTableSet,
                                                 &EconomyConstructed);
    sighted &= g_addDiamonds && api->MidHook(g_addDiamonds.get(), &DiamondsAdded);
    sighted &= g_removeDiamonds && api->MidHook(g_removeDiamonds.get(), &DiamondsRemoved);
    sighted &= g_bazaarSnapshot && api->MidHook(g_bazaarSnapshot.get(), &BazaarOpened);

    if (!sighted) {
        api->Log("diamonds: the economy component was not fully located in this build - the wallet "
                 "may not fill, and spending may not come off the grant");
    }

    if (!g_serialise || !api->Hook(reinterpret_cast<void*>(g_serialise.address()),
                                   reinterpret_cast<void*>(&SerialiseDetour),
                                   reinterpret_cast<void**>(&g_originalSerialise))) {
        api->Log("diamonds: the property writer was not found in this build - granted diamonds "
                 "would be written into saves, so the option is held at zero");
    }
}

int GetDiamonds() { return g_target; }

void SetDiamonds(int value) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (value == g_target) {
        return;
    }

    // Without the save hook a grant would become permanent, which is the one thing this must not do.
    g_target = (g_originalSerialise != nullptr) ? value : 0;

    if (g_target > 0) {
        FCSE::Logf("diamonds: %d", g_target);
    } else {
        api->Log("diamonds: off - any grant outstanding is handed back");
    }
}
