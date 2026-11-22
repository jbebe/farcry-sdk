// The rain component on the player, followed at the strength the environment manager hands it each
// frame. See docs/docs/engine-internals/time-of-day-and-lighting.md#rain.
#pragma once

namespace SkyOverhaul::RainComponent {

// Hooks the component's strength setter and its splashes. Call once from FCSE_Load. False means the
// setter was not found, which it logs, and the game's own drops stay.
bool Install();

// While set, the game's drop particles are given no strength and its splashes are not made, so
// nothing of theirs falls or lands. The component keeps its own strength, which its cover map,
// sound and fires going out run on.
void SetDropsHidden(bool hidden);

struct State {
    // Nought to one, eased by the manager.
    float strength;
    // Unit length, Z up, the way the drops fall and the cover map looks.
    float direction[3];
    bool started;
};

// The newest frame's, from any thread. False before the game has set a strength.
bool Latest(State& out);

}
