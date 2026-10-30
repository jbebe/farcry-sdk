// The player's guns, at the addresses of Steam's Dunia.dll. What each call does is in
// docs/docs/engine-internals/cheat-flags-and-economy.md.
#include "engine/inventory.h"

#include "engine/entity.h"
#include "engine/game_thread.h"
#include "engine/memory.h"
#include "engine/player.h"
#include "fcse_api.h"

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <mutex>

namespace {
    using DevTools::Method;
    using DevTools::Read;

    using GetWeaponFromCategoryFn = void*(__fastcall*)(void* inventory, void* unused, const uint32_t* category);
    using DropWeaponFn = void*(__fastcall*)(void* inventory, void* unused, void* gun, int keep);
    using AddWeaponByNameFn = bool(__fastcall*)(void* inventory, void* unused, const char* archetype);
    using ForceSelectWeaponFn = void(__fastcall*)(void* inventory, void* unused, const uint32_t* category);
    using GetEquippedWeaponFn = void*(__fastcall*)(void* view, void* unused);
    using SetAmmoFn = void(__fastcall*)(void* view, void* unused, const uint32_t* ammoType, int rounds);
    using RoundsFn = int(__fastcall*)(void* gun, void* unused);
    using SetRoundsFn = void(__fastcall*)(void* gun, void* unused, int rounds);
    using SetReliabilityFn = void(__fastcall*)(void* gun, void* unused, float reliability);

    FCSE::Relocation<GetWeaponFromCategoryFn> g_getWeaponFromCategory{FCSE::Uplay(0x00128FB0)};
    FCSE::Relocation<DropWeaponFn> g_dropWeapon{FCSE::Uplay(0x0012DB20)};
    FCSE::Relocation<AddWeaponByNameFn> g_addWeaponByName{FCSE::Uplay(0x0012B540)};
    FCSE::Relocation<ForceSelectWeaponFn> g_forceSelectWeapon{FCSE::Uplay(0x0012B9B0)};
    FCSE::Relocation<GetEquippedWeaponFn> g_getEquippedWeapon{FCSE::Uplay(0x00127DA0)};
    FCSE::Relocation<SetAmmoFn> g_setAmmo{FCSE::Uplay(0x00125F70)};
    FCSE::Relocation<RoundsFn> g_getMaxAmmo{FCSE::Uplay(0x0012F820)};
    FCSE::Relocation<RoundsFn> g_getRememberedAmmoInClip{FCSE::Uplay(0x0012EB50)};
    FCSE::Relocation<SetRoundsFn> g_setRememberedAmmoInClip{FCSE::Uplay(0x0012F850)};
    FCSE::Relocation<SetRoundsFn> g_forceAmmoInClip{FCSE::Uplay(0x00130060)};

    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kDataInventoryView = 0x4F0;
    constexpr ptrdiff_t kViewInventory = 0x04;
    constexpr ptrdiff_t kInventoryLocked = 0x50;
    constexpr ptrdiff_t kGunState = 0x24;
    constexpr ptrdiff_t kStateProperties = 0x04;
    constexpr ptrdiff_t kStateReliability = 0x0C;
    constexpr ptrdiff_t kStateMaxReliability = 0x10;
    constexpr ptrdiff_t kPropertiesAmmoType = 0x88;
    constexpr size_t kSetReliabilitySlot = 0xBC / sizeof(void*);

    bool g_ready = false;

    std::mutex g_resultLock;
    std::string g_result;

    void Report(const std::string& result) {
        FCSE::Logf("inventory: %s", result.c_str());
        std::lock_guard<std::mutex> held(g_resultLock);
        g_result = result;
    }

    std::string NameOf(void* gun) {
        return DevTools::Entity::Name(FCSE::ApiPointer()->EntityData->EntityOf(gun));
    }

    void Hand(const std::string& archetype, uint32_t category, int loaded, int spare, float health) {
        void* pawn = DevTools::Player::Pawn();
        if (pawn == nullptr) {
            Report("no player to give " + archetype + " to");
            return;
        }
        void* view = Read<uint8_t*>(pawn, kPawnData) + kDataInventoryView;
        void* inventory = Read<void*>(view, kViewInventory);

        void* gun = g_getWeaponFromCategory(inventory, nullptr, &category);
        while (gun != nullptr && NameOf(gun) != archetype) {
            g_dropWeapon(inventory, nullptr, gun, 0);
            void* next = g_getWeaponFromCategory(inventory, nullptr, &category);
            // The engine refuses a drop while the player is on a mounted gun.
            if (next == gun) {
                Report("the player cannot drop the " + NameOf(gun) + " right now");
                return;
            }
            gun = next;
        }
        if (gun == nullptr) {
            if (!g_addWeaponByName(inventory, nullptr, archetype.c_str())) {
                Report("this world's library has no " + archetype);
                return;
            }
            gun = g_getWeaponFromCategory(inventory, nullptr, &category);
            if (gun == nullptr) {
                Report(archetype + " was added, but this game's data puts it in another class");
                return;
            }
        }

        const void* state = Read<const void*>(gun, kGunState);
        const float most = Read<float>(state, kStateMaxReliability);
        Method<SetReliabilityFn>(gun, kSetReliabilitySlot)(gun, nullptr, most * health / 100.0f);

        // A gun loads what it remembers as it is drawn, so one already in hand is loaded as well.
        g_setRememberedAmmoInClip(gun, nullptr, loaded);
        const int magazine = g_getRememberedAmmoInClip(gun, nullptr);
        if (g_getEquippedWeapon(view, nullptr) == gun) {
            g_forceAmmoInClip(gun, nullptr, magazine);
        }

        const auto* properties = Read<const uint8_t*>(state, kStateProperties);
        const int pool = std::min(spare, g_getMaxAmmo(gun, nullptr));
        g_setAmmo(view, nullptr, reinterpret_cast<const uint32_t*>(properties + kPropertiesAmmoType), pool);

        if (!Read<bool>(inventory, kInventoryLocked)) {
            g_forceSelectWeapon(inventory, nullptr, &category);
        }

        char result[160];
        std::snprintf(result, sizeof(result), "%s: %d in the magazine, %d spare, reliability %.0f of %.0f",
                      archetype.c_str(), magazine, pool, Read<float>(state, kStateReliability), most);
        Report(result);
    }
}

namespace DevTools::Inventory {

void Install() {
    g_ready = g_getWeaponFromCategory && g_dropWeapon && g_addWeaponByName && g_forceSelectWeapon &&
              g_getEquippedWeapon && g_setAmmo && g_getMaxAmmo && g_getRememberedAmmoInClip &&
              g_setRememberedAmmoInClip && g_forceAmmoInClip;
    if (!g_ready) {
        Report("not everything needed to give a gun was found in this build");
    }
}

void Give(const char* archetype, const char* category, int loaded, int spare, float health) {
    if (!g_ready) {
        return;
    }
    GameThread::Post([name = std::string(archetype), id = FCSE::Crc32(category), loaded, spare, health] {
        Hand(name, id, loaded, spare, health);
    });
}

std::string LastResult() {
    std::lock_guard<std::mutex> held(g_resultLock);
    return g_result;
}

}
