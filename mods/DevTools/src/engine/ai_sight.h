// What each soldier sees of the player, copied out of his vision updates for another thread to draw.
//
// CSensorySystem::AddTargetDataToKB is where one update has a soldier's cones, eyes and memory of
// one target in hand, after every adjustment to the cones. See docs/docs/engine-internals/ai.md.
#pragma once

#include <array>
#include <vector>

namespace DevTools::AiSight {

using Point = std::array<float, 3>;

struct Sight {
    // Where he stands, where his eyes are and the way his head points, not normalised, and the
    // player's head as the update measured it.
    Point feet;
    Point eye;
    Point view;
    Point target;
    // The focus cone's length as adjusted and its angle as his archetype gives it, and the
    // peripheral cone's length and angle as adjusted; lengths in metres, full angles in degrees.
    float focusLength;
    float baseFocusAngle;
    float peripheralLength;
    float peripheralAngle;
    // The visibility from which he senses something and from which he sees the player, as his
    // current state sets them.
    float fuzzy;
    float clear;
    // His memory of the player: whether he has one, how visible the player is to him (0 to 1,
    // smoothed), whether he sees him plainly, inside both cones, and where he last perceived him.
    bool known;
    float visibility;
    bool plainlySeen;
    Point believed;
};

// Replaces `out` with every soldier whose vision updated in the last second. Copying runs only while
// this is being called. Safe from any thread.
void Snapshot(std::vector<Sight>& out);

// Hooks the vision update. Call once from FCSE_Load; logs if the update is not found.
void Install();

}
