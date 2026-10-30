// The local player's guns: handing one over the way a pickup does, and setting the one in hand.
#pragma once

#include <string>

namespace DevTools::Inventory {

// The gun in the player's hands as the last frame left it. `capacity` is what its magazine and spare
// pool hold together, which is 0 for the machete and when nothing is in hand. `health` is `reliability`
// as a percentage of `maxReliability`.
struct Held {
    std::string name;
    int magazine = 0;
    int spare = 0;
    int capacity = 0;
    float health = 0.0f;
    float reliability = 0.0f;
    float maxReliability = 0.0f;
};

void Install();

// Puts the gun `archetype` in the player's hands at the end of the next frame, full and new. The gun
// carried in the class `category`, whose CRC-32 is the engine's id for it, drops at their feet, unless
// it is this model, which is kept and restored instead.
void Give(const char* archetype, const char* category);

// Sets the gun in hand at the end of the next frame. Rounds fill the magazine first and the rest go
// spare; health is a percentage of the gun's maximum reliability.
void SetRounds(int rounds);
void SetHealth(float percent);

// What the last frame saw in the player's hands, asking the next frame to look again.
Held InHand();

// What the last Give did, in words.
std::string LastResult();

}
