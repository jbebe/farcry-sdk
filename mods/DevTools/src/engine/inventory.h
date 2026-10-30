// The local player's guns: handing one over, the way a pickup does.
#pragma once

#include <string>

namespace DevTools::Inventory {

void Install();

// Puts the gun `archetype` in the player's hands at the end of the next frame. The gun carried in the
// class `category`, whose CRC-32 is the engine's id for it, drops at their feet, unless it is this
// model, which is kept and set instead. Rounds are capped at what the gun holds.
void Give(const char* archetype, const char* category, int loaded, int spare, float health);

// What the last Give did, in words.
std::string LastResult();

}
