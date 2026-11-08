// A puddle under the dead: a soldier killed after a bullet to the head, or three anywhere else, leaves a
// puddle of blood that seeps in where he stood.
#include "engine/damage.h"
#include "engine/game.h"
#include "engine/impacts.h"
#include "engine/memory.h"
#include "engine/ray.h"
#include "fcse_api.h"

#include <cstdint>
#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    Damage::StimHandlerFn g_originalSoldierDamage = nullptr;

    // The bullets a living soldier has taken, a head hit counting as kBodyHits.
    std::unordered_map<const void*, int> g_wounds;

    constexpr uint32_t kPuddle = FCSE::Crc32("Blood.Puddle");
    constexpr int kBodyHits = 3;
    // The ray down to the ground, from this high above his feet.
    constexpr float kAbove = 0.5f;
    constexpr float kReach = 1.5f;
    constexpr float kDown[3] = {0.0f, 0.0f, -1.0f};

    void __fastcall SoldierDamageDetour(void* counters, void* unused, const uint8_t* stim) {
        const float before = Damage::Health(counters);
        g_originalSoldierDamage(counters, unused, stim);
        if (Damage::Type(stim) != Damage::kPierce || !(before > 0.0f)) {
            return;
        }
        int& wounds = Remember(g_wounds, counters);
        wounds += Damage::Part(stim) == Damage::kHead ? kBodyHits : 1;
        if (Damage::Health(counters) > 0.0f) {
            return;
        }
        float at[3];
        Game::Position(Game::EntityOf(counters), at);
        at[2] += kAbove;
        Ray::Hit ground;
        if (wounds >= kBodyHits && Ray::FirstSurface(at, kDown, kReach, ground)) {
            Impacts::SpawnDecal(Impacts::Record(kPuddle), ground);
        }
        g_wounds.erase(counters);
    }
}

void InstallBloodPuddle() {
    if (!Impacts::Install() || !Hook(Damage::SoldierDamage, &SoldierDamageDetour, &g_originalSoldierDamage)) {
        FCSE::Logf("blood puddle: the soldiers' damage handling was not found in this build, so the dead leave no "
                   "puddle");
    }
}
