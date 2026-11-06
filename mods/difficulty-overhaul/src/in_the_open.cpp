// In the open: tells each soldier's brain, on an agent flag the layer's brain reads, whether the player
// is in the open for him, which decides whether he holds to fire or makes for cover.
#include "engine/game.h"
#include "engine/memory.h"
#include "engine/shooting.h"
#include "fcse_api.h"
#include "perception.h"

using namespace DifficultyOverhaul;

namespace {
    Shooting::UpdateFn g_originalUpdate = nullptr;

    // The agent flag the layer's brain reads, which no soldier's brain or code otherwise touches.
    constexpr int kInTheOpenFlag = 34;

    void __fastcall UpdateDetour(void* shooting, void* unused, float seconds) {
        g_originalUpdate(shooting, unused, seconds);
        void* agent = Shooting::Agent(shooting);
        const bool open = Shooting::Alert(agent) && Shooting::AimsAtPlayer(shooting) &&
                          Perception::InTheOpen(Shooting::Id(agent), Game::Now());
        Shooting::SetFlag(agent, kInTheOpenFlag, open);
    }
}

void InstallInTheOpen() {
    if (!Shooting::Found() || !Game::ClockFound() || !Hook(Shooting::Update, &UpdateDetour, &g_originalUpdate)) {
        FCSE::Logf("in the open: the shooting system's update was not found in this build, so soldiers make for "
                   "cover as the game has them");
    }
}
