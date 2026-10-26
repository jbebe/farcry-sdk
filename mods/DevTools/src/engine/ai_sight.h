// What each soldier sees of the player, copied out of his vision updates for another thread to draw.
//
// CSensorySystem::AddTargetDataToKB is where one update has a soldier's cones, eyes and memory of
// one target in hand, after every adjustment to the cones. See docs/docs/engine-internals/ai.md.
#pragma once

#include <vector>

namespace DevTools::AiSight {

struct Sight {
    // Where his eyes are and the way his head points, not normalised, and the player's head as the
    // update measured it.
    float eye[3];
    float view[3];
    float target[3];
    // The cones the update used, each a length in metres and a full angle in degrees, and the focus
    // angle his archetype gives before anything widens it.
    float focusLength;
    float focusAngle;
    float peripheralLength;
    float peripheralAngle;
    float baseFocusAngle;
    // His memory of the player: whether he has one, how visible the player is to him (0 to 1,
    // smoothed), whether he sees him plainly, inside both cones, and where he last perceived him.
    bool known;
    float visibility;
    bool plainlySeen;
    float believed[3];
};

// Starts or stops copying. Off, which it starts as, a vision update costs one test more.
void SetEnabled(bool enabled);

// Every soldier whose vision updated in the last second. Safe from any thread.
std::vector<Sight> Snapshot();

// Hooks the vision update. Call once from FCSE_Load; logs if the update is not found.
void Install();

}
