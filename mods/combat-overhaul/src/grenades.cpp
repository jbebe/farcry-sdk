// Grenades: every soldier carries one grenade and no more. One fighting the player throws it at where he last
// saw him once the player has kept behind cover a few seconds, one soldier at a time.
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"
#include "perception.h"

#include <algorithm>
#include <cstdint>
#include <iterator>
#include <unordered_map>

using namespace CombatOverhaul;

namespace {
    using SpawnFn = void(__fastcall*)(void* inventory, void* unused);
    using GadgetFn = void*(__fastcall*)(const void* view, void* unused, const uint32_t* category);

    // SInventoryViewPawnImpl::OnSpawn, which equips a pawn with its inventory pack.
    FCSE::Relocation<SpawnFn> g_spawn{FCSE::Uplay(0x00126C00)};
    SpawnFn g_originalSpawn = nullptr;
    Shooting::UpdateFn g_originalUpdate = nullptr;
    // CInventoryViewPawn::GetGadgetFromCategory.
    FCSE::Relocation<GadgetFn> g_gadget{FCSE::Uplay(0x00129DF0)};

    // The inventory's pack, the CRC32 of its name, and its pawn; a gadget's state, its uses and whether a use
    // spends none, as every soldier's grenade has it.
    constexpr ptrdiff_t kInventoryPack = 0x08;
    constexpr ptrdiff_t kInventoryPawn = 0x48;
    constexpr ptrdiff_t kGadgetState = 0x24;
    constexpr ptrdiff_t kStateUses = 0x7C;
    constexpr ptrdiff_t kStateUnlimited = 0x80;
    constexpr uint32_t kGrenade = 0x6AE0E3B1;
    // The enemy soldiers' packs, three grenades each: assault, shotgun, sniper, RocketLauncher, Mortar and
    // CarlGustav.
    constexpr uint32_t kSoldierPacks[] = {0xCCE9D60C, 0xEEAE53E1, 0xAE2848CB, 0x6F2D03DF, 0x4945BAE0, 0xB3E1E534};

    // The agent flag the layer's brain throws on, which no soldier's brain or code otherwise touches.
    constexpr int kThrowFlag = 35;
    // Hardcore: how long the player keeps behind cover before a throw, how long since the soldier last saw
    // him at most, how far off the spot he saw him at may be, and how long after one throw the next.
    constexpr double kBehindCoverSeconds = 3.0;
    constexpr double kKnownSeconds = 20.0;
    constexpr float kNearestThrow = 8.0f;
    constexpr float kFarthestThrow = 30.0f;
    constexpr double kBetweenThrows = 10.0;
    // How long the brain has to take up a throw, and to make it before he may be asked again.
    constexpr double kAskSeconds = 1.0;
    constexpr double kThrowSeconds = 5.0;

    // A soldier fighting the player: whether he carries a grenade, when he last had the player in the open,
    // from when he was first fighting him, when and where he last saw him, and when he was asked to throw.
    struct Soldier {
        bool carries = false;
        double open = -1.0;
        double seen = -1.0e9;
        float spot[3] = {};
        double asked = -1.0;
    };
    std::unordered_map<const void*, Soldier> g_soldiers;
    double g_lastAsked = -1.0e9;

    // Whether an inventory is an enemy soldier's, by its pack.
    bool SoldierPack(const void* inventory) {
        const auto pack = Read<uint32_t>(inventory, kInventoryPack);
        return std::find(std::begin(kSoldierPacks), std::end(kSoldierPacks), pack) != std::end(kSoldierPacks);
    }

    // The state of the grenade in a pawn's inventory, or null when it has none, and how many it has left.
    uint8_t* Grenade(void* view) {
        void* gadget = g_gadget(view, nullptr, &kGrenade);
        return gadget != nullptr ? Read<uint8_t*>(gadget, kGadgetState) : nullptr;
    }

    int Left(const uint8_t* grenade) { return grenade != nullptr ? Read<int>(grenade, kStateUses) : 0; }

    // A soldier spawns with one grenade where the game gives three, and his throw spends it. A spent grenade
    // stays in his inventory, so the game's own drop when he dies is the same whether he threw or not.
    void __fastcall SpawnDetour(void* inventory, void* unused) {
        g_originalSpawn(inventory, unused);
        void* pawn = Read<void*>(inventory, kInventoryPawn);
        if (!SoldierPack(inventory) || pawn == nullptr) {
            return;
        }
        uint8_t* grenade = Grenade(Shooting::InventoryView(pawn));
        if (grenade == nullptr) {
            return;
        }
        Write<int>(grenade, kStateUses, 1);
        Write<bool>(grenade, kStateUnlimited, false);
    }

    // Asks a soldier with his grenade to throw once the player has kept behind cover and he knows where; the
    // layer's brain clears the flag as it takes the throw up, and the plugin takes back one it left.
    void __fastcall UpdateDetour(void* shooting, void* unused, float seconds) {
        g_originalUpdate(shooting, unused, seconds);
        void* agent = Shooting::Agent(shooting);
        float from[3];
        float to[3];
        if (!Shooting::Fighting(agent) || !Shooting::AimsAtPlayer(shooting) || !Shooting::Positions(shooting, from, to)) {
            if (g_soldiers.erase(agent) != 0) {
                Shooting::SetFlag(agent, kThrowFlag, false);
            }
            return;
        }
        const double now = Game::Now();
        void* view = Shooting::InventoryView(Shooting::Pawn(agent));
        Soldier& soldier = Remember(g_soldiers, agent);
        if (soldier.open < 0.0) {
            soldier.open = now;
            soldier.carries = Left(Grenade(view)) > 0;
        }
        if (!soldier.carries) {
            return;
        }
        const uint32_t id = Shooting::Id(agent);
        if (Perception::InTheOpen(id, now)) {
            soldier.open = now;
        }
        if (Shooting::Visibility(shooting) > 0.0f) {
            soldier.seen = now;
            std::copy_n(to, 3, soldier.spot);
        }
        if (soldier.asked >= 0.0) {
            if (now - soldier.asked >= kAskSeconds) {
                Shooting::SetFlag(agent, kThrowFlag, false);
            }
            if (now - soldier.asked < kThrowSeconds) {
                return;
            }
            soldier.asked = -1.0;
            soldier.carries = Left(Grenade(view)) > 0;
            if (soldier.carries) {
                FCSE::Sendf("combat", "grenades: soldier %08X was asked to throw and did not", id);
            }
        }
        const double hidden = now - soldier.open;
        const float distance = Game::Distance(from, soldier.spot);
        if (hidden < kBehindCoverSeconds || now - soldier.seen > kKnownSeconds || now - g_lastAsked < kBetweenThrows ||
            distance < kNearestThrow || distance > kFarthestThrow || Left(Grenade(view)) <= 0) {
            return;
        }
        Shooting::SetFlag(agent, kThrowFlag, true);
        soldier.asked = now;
        g_lastAsked = now;
        FCSE::Sendf("combat", "grenades: soldier %08X throws at where he saw the player %.1f s ago, %.1f m off, %.1f s "
                    "behind cover", id, now - soldier.seen, distance, hidden);
    }
}

void InstallGrenades() {
    if (!g_gadget || !Hook(g_spawn, &SpawnDetour, &g_originalSpawn)) {
        FCSE::Logf("grenades: the inventory's spawn was not found in this build, so soldiers carry the game's three");
    }
    if (!g_gadget || !Shooting::Found() || !Game::ClockFound() ||
        !Hook(Shooting::Update, &UpdateDetour, &g_originalUpdate)) {
        FCSE::Logf("grenades: the shooting system's update was not found in this build, so soldiers throw only as "
                   "the game has them");
    }
}
