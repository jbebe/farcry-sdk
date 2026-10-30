// The player's guns, at the addresses of Steam's Dunia.dll. What each call does is in
// docs/docs/engine-internals/cheat-flags-and-economy.md.
#include "engine/inventory.h"

#include "engine/entity.h"
#include "engine/game_thread.h"
#include "engine/memory.h"
#include "engine/player.h"
#include "fcse_api.h"

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <utility>

namespace {
    using DevTools::Method;
    using DevTools::Read;
    using DevTools::Inventory::Held;

    using GetWeaponFromCategoryFn = void*(__fastcall*)(void* inventory, void* unused, const uint32_t* category);
    using DropWeaponFn = void*(__fastcall*)(void* inventory, void* unused, void* gun, int keep);
    using AddWeaponByNameFn = bool(__fastcall*)(void* inventory, void* unused, const char* archetype);
    using ForceSelectWeaponFn = void(__fastcall*)(void* inventory, void* unused, const uint32_t* category);
    using GetEquippedWeaponFn = void*(__fastcall*)(void* view, void* unused);
    using GetAmmoFn = int(__fastcall*)(void* view, void* unused, const uint32_t* ammoType);
    using SetAmmoFn = void(__fastcall*)(void* view, void* unused, const uint32_t* ammoType, int rounds);
    using RoundsFn = int(__fastcall*)(void* gun, void* unused);
    using SetRoundsFn = void(__fastcall*)(void* gun, void* unused, int rounds);
    using SetReliabilityFn = void(__fastcall*)(void* gun, void* unused, float reliability);

    FCSE::Relocation<GetWeaponFromCategoryFn> g_getWeaponFromCategory{FCSE::Uplay(0x00128FB0)};
    FCSE::Relocation<DropWeaponFn> g_dropWeapon{FCSE::Uplay(0x0012DB20)};
    FCSE::Relocation<AddWeaponByNameFn> g_addWeaponByName{FCSE::Uplay(0x0012B540)};
    FCSE::Relocation<ForceSelectWeaponFn> g_forceSelectWeapon{FCSE::Uplay(0x0012B9B0)};
    FCSE::Relocation<GetEquippedWeaponFn> g_getEquippedWeapon{FCSE::Uplay(0x00127DA0)};
    FCSE::Relocation<GetAmmoFn> g_getAmmo{FCSE::Uplay(0x00126000)};
    FCSE::Relocation<SetAmmoFn> g_setAmmo{FCSE::Uplay(0x00125F70)};
    FCSE::Relocation<RoundsFn> g_getMaxAmmo{FCSE::Uplay(0x0012F820)};
    FCSE::Relocation<RoundsFn> g_getMaxAmmoInClip{FCSE::Uplay(0x0012F830)};
    FCSE::Relocation<RoundsFn> g_getAmmoInClip{FCSE::Uplay(0x0012E950)};
    FCSE::Relocation<SetRoundsFn> g_setAmmoInClip{FCSE::Uplay(0x0012E960)};
    FCSE::Relocation<SetRoundsFn> g_setRememberedAmmoInClip{FCSE::Uplay(0x0012F850)};

    constexpr ptrdiff_t kPawnData = 0x10;
    constexpr ptrdiff_t kDataInventoryView = 0x4F0;
    constexpr ptrdiff_t kViewInventory = 0x04;
    constexpr ptrdiff_t kInventoryLocked = 0x50;
    constexpr ptrdiff_t kGunState = 0x24;
    constexpr ptrdiff_t kStateProperties = 0x04;
    constexpr ptrdiff_t kStateReliability = 0x0C;
    constexpr ptrdiff_t kStateMaxReliability = 0x10;
    constexpr ptrdiff_t kPropertiesAmmoType = 0x88;
    constexpr ptrdiff_t kPropertiesUsesClips = 0xA4;
    constexpr size_t kSetReliabilitySlot = 0xBC / sizeof(void*);

    bool g_ready = false;

    std::mutex g_lock;
    std::string g_result;
    Held g_held;
    std::atomic<bool> g_lookQueued{false};

    void Report(const std::string& result) {
        FCSE::Logf("inventory: %s", result.c_str());
        std::lock_guard<std::mutex> held(g_lock);
        g_result = result;
    }

    std::string NameOf(void* gun) {
        return DevTools::Entity::Name(FCSE::ApiPointer()->EntityData->EntityOf(gun));
    }

    void* PlayerView() {
        void* pawn = DevTools::Player::Pawn();
        return pawn != nullptr ? Read<uint8_t*>(pawn, kPawnData) + kDataInventoryView : nullptr;
    }

    void* GunInHand(void* view) { return view != nullptr ? g_getEquippedWeapon(view, nullptr) : nullptr; }

    const void* StateOf(void* gun) { return Read<const void*>(gun, kGunState); }

    const uint8_t* PropertiesOf(void* gun) { return Read<const uint8_t*>(StateOf(gun), kStateProperties); }

    const uint32_t* AmmoTypeOf(void* gun) {
        return reinterpret_cast<const uint32_t*>(PropertiesOf(gun) + kPropertiesAmmoType);
    }

    // A gun without clips keeps its rounds in the magazine alone, so it has no spare to add.
    int CapacityOf(void* gun) {
        const int magazine = g_getMaxAmmoInClip(gun, nullptr);
        return Read<bool>(PropertiesOf(gun), kPropertiesUsesClips) ? magazine + g_getMaxAmmo(gun, nullptr) : magazine;
    }

    void SetHealthOf(void* gun, float percent) {
        const float most = Read<float>(StateOf(gun), kStateMaxReliability);
        Method<SetReliabilityFn>(gun, kSetReliabilitySlot)(gun, nullptr, most * percent / 100.0f);
    }

    // For the gun in hand, which has already loaded what it remembers.
    void SetRoundsOf(void* view, void* gun, int rounds) {
        rounds = std::clamp(rounds, 0, CapacityOf(gun));
        const int magazine = std::min(rounds, g_getMaxAmmoInClip(gun, nullptr));
        g_setAmmoInClip(gun, nullptr, magazine);
        g_setAmmo(view, nullptr, AmmoTypeOf(gun), rounds - magazine);
    }

    void Hand(const std::string& archetype, uint32_t category) {
        void* view = PlayerView();
        if (view == nullptr) {
            Report("no player to give " + archetype + " to");
            return;
        }
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

        SetHealthOf(gun, 100.0f);
        if (GunInHand(view) == gun) {
            SetRoundsOf(view, gun, CapacityOf(gun));
        } else {
            // Drawing loads what the gun remembers, or for a gun without clips its whole pool.
            g_setRememberedAmmoInClip(gun, nullptr, g_getMaxAmmoInClip(gun, nullptr));
            g_setAmmo(view, nullptr, AmmoTypeOf(gun), g_getMaxAmmo(gun, nullptr));
        }
        if (!Read<bool>(inventory, kInventoryLocked)) {
            g_forceSelectWeapon(inventory, nullptr, &category);
        }
        Report(archetype + ": full and new");
    }

    void Look() {
        g_lookQueued = false;
        Held held;
        void* view = PlayerView();
        if (void* gun = GunInHand(view)) {
            const void* state = StateOf(gun);
            held.name = NameOf(gun);
            held.magazine = g_getAmmoInClip(gun, nullptr);
            held.spare = g_getAmmo(view, nullptr, AmmoTypeOf(gun));
            held.capacity = CapacityOf(gun);
            held.reliability = Read<float>(state, kStateReliability);
            held.maxReliability = Read<float>(state, kStateMaxReliability);
            held.health = held.maxReliability > 0.0f ? held.reliability / held.maxReliability * 100.0f : 0.0f;
        }
        std::lock_guard<std::mutex> locked(g_lock);
        g_held = std::move(held);
    }

    template <class Job>
    void PostToGunInHand(Job job) {
        if (g_ready) {
            DevTools::GameThread::Post([job] {
                void* view = PlayerView();
                if (void* gun = GunInHand(view)) {
                    job(view, gun);
                }
            });
        }
    }
}

namespace DevTools::Inventory {

void Install() {
    g_ready = g_getWeaponFromCategory && g_dropWeapon && g_addWeaponByName && g_forceSelectWeapon &&
              g_getEquippedWeapon && g_getAmmo && g_setAmmo && g_getMaxAmmo && g_getMaxAmmoInClip &&
              g_getAmmoInClip && g_setAmmoInClip && g_setRememberedAmmoInClip;
    if (!g_ready) {
        Report("not everything needed to give a gun was found in this build");
    }
}

void Give(const char* archetype, const char* category) {
    if (g_ready) {
        GameThread::Post([name = std::string(archetype), id = FCSE::Crc32(category)] { Hand(name, id); });
    }
}

void SetRounds(int rounds) {
    PostToGunInHand([rounds](void* view, void* gun) { SetRoundsOf(view, gun, rounds); });
}

void SetHealth(float percent) {
    PostToGunInHand([percent](void*, void* gun) { SetHealthOf(gun, percent); });
}

Held InHand() {
    if (g_ready && !g_lookQueued.exchange(true)) {
        GameThread::Post(Look);
    }
    std::lock_guard<std::mutex> held(g_lock);
    return g_held;
}

std::string LastResult() {
    std::lock_guard<std::mutex> held(g_lock);
    return g_result;
}

}
