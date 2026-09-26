// What the Sound Overhaul window mutes, so the rest of a fight can be heard on its own.
#pragma once

namespace SoundOverhaul::Mutes {

// Every weapon's single shot, the player's and everyone else's.
inline bool singleShots = false;
// Full-auto fire loops and their tails.
inline bool autoFire = false;
// Gunshot echoes.
inline bool echoes = false;

// Hooks the engine's single-shot and auto-fire PlaySound calls; echoes are muted in last_echo.cpp.
void Install();

// Draws the window in DevTools' overlay.
void DrawWindow(void* userData);

}
