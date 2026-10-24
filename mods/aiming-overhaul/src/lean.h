// Leaning: held Q or E slides the upper body out to the side and bends it from the belly, and the
// head, the view, the arms and the gun go with it; see
// docs/docs/engine-internals/first-person-aiming.md#leaning.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Lean {

// Follows the lean signals: a lean key tapped leans until it is tapped again, and held, for as long
// as it is held.
void OnSignal(uint32_t signal);

// Leans the pawn's body this frame, given the seconds since the last and whether the player may
// lean now. Returns how far the view rolls with it, in radians, right positive.
float Update(uint8_t* pawn, float seconds, bool free);

// Bends a skeleton freshly posed, if it is the player's.
void OnPosed(uint8_t* skeleton);

// Bends what is placed from the player's view bone with the body, though the bone itself stays
// unbent: the camera, and a long gun at the sights. True where it bent the copy.
bool OnAttached(uint8_t* skeleton, int bone, float* model);

}
