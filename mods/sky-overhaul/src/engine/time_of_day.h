// The game's clock: read from the environment manager, and set through DevTools' console.
#pragma once

namespace SkyOverhaul::TimeOfDay {

// Finds the environment manager. Call once from FCSE_Load. False means it was not found, which it
// logs, and Elapsed will always be false.
bool Install();

// Seconds of game clock since the playthrough's first midnight, counted days included. Call once a
// frame, and only while a world is drawn.
bool Elapsed(double& seconds);

// How many clock seconds pass in one second of game time, or 0 when the manager was not found.
float Scale();

// Sets the clock to minutes past midnight, from which the day runs on. Does nothing without DevTools.
void Set(int minutes);

}
