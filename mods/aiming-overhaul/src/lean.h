// Leaning: held Q or E bends the body out to the side from the belly, and the head, the view, the
// arms and the gun go with it; see docs/docs/engine-internals/first-person-aiming.md#leaning.
#pragma once

#include "engine/aim.h"

#include <cstdint>

namespace AimingOverhaul::Lean {

// Follows the lean signals.
void OnSignal(uint32_t signal);

// Leans the pawn's body this frame, given the seconds since the last and whether the player may
// lean now. Returns how far the bend has moved the eye, in the view's axes.
Aim::Offset Update(uint8_t* pawn, float seconds, bool free);

// Bends a skeleton freshly posed, if it is the player's.
void OnPosed(uint8_t* skeleton);

void SetEnabled(bool enabled);

}
