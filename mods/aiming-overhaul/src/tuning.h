// How each weapon's shots shake the view, kept in bin\aiming-overhaul.ini, a section per weapon, and
// edited in Aiming Overhaul's window in DevTools' overlay.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Tuning {

// Degrees for each newton-second of a round's push, and seconds; every weapon's until the file gives
// it its own.
struct Values {
    float kickPerImpulse = 0.149f;
    float kickTime = 0.4f;
    float kickDelay = 0.1f;
    float rollPerImpulse = 0.103f;
    float rollCycle = 0.08f;
    float rollFade = 0.11f;
};

// Follows the weapon in hand, or null, taking its values each time it changes.
void Follow(uint8_t* weapon);

// The weapon in hand's values, which the window edits in place.
const Values& InHand();

// Reads bin\aiming-overhaul.ini over the defaults and writes it back complete.
void Load();

// The window's contents, for DevTools' overlay.
void DrawWindow(void*);

}
