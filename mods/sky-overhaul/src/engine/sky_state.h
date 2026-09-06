// The sun, published by the game thread as the engine submits the sky.
//
// The sky's draws are submitted as packets on the game thread and executed later on the render
// thread, so nothing here touches Direct3D. See docs/docs/engine-internals/sky-and-clouds.md.
#pragma once

#include <cstdint>

namespace SkyOverhaul::SkyState {

// The sun as the sky renderer had it when it submitted the sun disc.
struct Sun {
    // Unit length, Z up.
    float direction[3];
    // Zero in clear weather, one in a full storm.
    float storm;
};

// Hooks the sun-disc submission. Call once from FCSE_Load. False means the sun is unavailable,
// which it logs, and nothing is left hooked.
bool Install();

// The newest complete snapshot. False until the engine has submitted a sun.
bool Latest(Sun& out);

// Counts sun submissions. A render frame whose count differs from the previous one had a world
// drawn into it, which is what tells a menu or a loading screen from the game.
uint32_t SubmitCount();

// The thread submission runs on. Zero until the first submission.
unsigned long SubmitThreadId();

}
