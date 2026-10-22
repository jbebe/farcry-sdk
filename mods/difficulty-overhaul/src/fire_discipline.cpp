// Fire discipline: a soldier holds his fire at the player until he sees the player clearly.
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"

using namespace DifficultyOverhaul;

namespace {
    Shooting::CanShootFn g_originalCanShoot = nullptr;

    // Hardcore: how visible the player must be to him before he fires.
    constexpr float kFireVisibility = 0.4f;

    bool __fastcall CanShootDetour(void* task) {
        if (!g_originalCanShoot(task)) {
            return false;
        }
        void* shooting = Shooting::OfTask(task);
        return !Shooting::AimsAtPlayer(shooting) ||
               Shooting::Visibility(shooting) >= kFireVisibility;
    }
}

void InstallFireDiscipline() {
    if (!Shooting::Found() || !Hook(Shooting::CanShoot, &CanShootDetour, &g_originalCanShoot)) {
        FCSE::Logf("fire discipline: the soldiers' shoot task was not found in this build");
    }
}
