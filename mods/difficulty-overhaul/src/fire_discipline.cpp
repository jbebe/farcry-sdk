// Fire discipline: a soldier holds his fire at the player until he can make the player out, and
// waits aimed where he is rather than giving up the shot; one a hit has panicked fires regardless.
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

#include <unordered_map>

using namespace DifficultyOverhaul;

namespace {
    Shooting::TaskCheckFn g_originalIsAligned = nullptr;

    // Whether each soldier last held his fire at the player or fired, so only changes are logged.
    enum class Stance { kUnknown, kFires, kHolds };
    std::unordered_map<const void*, Stance> g_stances;

    bool __fastcall IsAlignedDetour(void* task) {
        if (!g_originalIsAligned(task)) {
            return false;
        }
        void* shooting = Shooting::OfTask(task);
        if (!Shooting::AimsAtPlayer(shooting) || Shooting::Panicking(shooting)) {
            return true;
        }
        const float visibility = Shooting::Visibility(shooting);
        const Stance stance =
            visibility < Shooting::FuzzyVisibility(shooting) ? Stance::kHolds : Stance::kFires;
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
    if (!Shooting::Found() || !Hook(Shooting::IsAligned, &IsAlignedDetour, &g_originalIsAligned)) {
        FCSE::Logf("fire discipline: the soldiers' shoot task was not found in this build");
    }
}
