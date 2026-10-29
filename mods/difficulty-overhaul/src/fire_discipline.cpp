// Fire discipline: a soldier holds his fire at the player until he sees the player clearly.
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    Shooting::CanShootFn g_originalCanShoot = nullptr;

    // Whether each soldier last held his fire at the player or fired, so only changes are logged.
    enum class Stance { kUnknown, kFires, kHolds };
    std::unordered_map<const void*, Stance> g_stances;

    bool __fastcall CanShootDetour(void* task) {
        if (!g_originalCanShoot(task)) {
            return false;
        }
        void* shooting = Shooting::OfTask(task);
        if (!Shooting::AimsAtPlayer(shooting)) {
            return true;
        }
        const float visibility = Shooting::Visibility(shooting);
        const Stance stance = visibility < Shooting::kClearVisibility ? Stance::kHolds : Stance::kFires;
        Stance& last = Remember(g_stances, shooting);
        if (stance != last) {
            last = stance;
            FCSE::Logf("fire discipline: %s at %.1f m, visibility %.2f",
                       stance == Stance::kHolds ? "holds his fire" : "fires",
                       Shooting::TargetDistance(shooting), visibility);
        }
        return stance == Stance::kFires;
    }
}

void InstallFireDiscipline() {
    if (!Shooting::Found() || !Hook(Shooting::CanShoot, &CanShootDetour, &g_originalCanShoot)) {
        FCSE::Logf("fire discipline: the soldiers' shoot task was not found in this build");
    }
}
