// The cheat flags the game profile carries.
//
// Three ints the engine reads all over: the same ones the -GameProfile_* launch flags and the
// cheat_* console commands set. Writing them is all god mode, unlimited ammo and unlocked weapons
// are as far as the profile is concerned - what those flags do not reach is each option's own
// problem. See docs/docs/engine-internals/cheat-flags-and-economy.md.
#pragma once

#include <cstdint>

namespace DevTools::GameProfile {

enum class Cheat { GodMode, UnlimitedAmmo, AllWeaponsUnlock, Count };

// Finds the profile pointer and takes a frame to write on. Call once from FCSE_Load.
void Install();

// Asks for a flag to be held at `on`. While on it is re-written every frame, because a profile
// reload puts the file's value back and an MP spectator clears GodMode outright. Turning it off
// writes zero once and then leaves the field alone - so a flag this plugin never set, such as one
// from a -GameProfile_* launch argument, is never touched.
void Want(Cheat cheat, bool on);

// The live field, or null until a profile exists. For the rare caller that has to change a flag
// around a single call rather than hold it.
int32_t* Field(Cheat cheat);

}
