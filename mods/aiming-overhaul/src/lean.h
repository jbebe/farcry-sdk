// Leaning: held Q or E slides the upper body out to the side and bends it from the belly, and the
// head, the view, the arms and the gun go with it; see
// docs/docs/engine-internals/first-person-aiming.md#leaning.
#pragma once

#include "engine/aim.h"

#include <cstdint>

namespace AimingOverhaul::Lean {

// Follows the lean signals.
void OnSignal(uint32_t signal);

// What the lean does to the view: how far it moves the eye, in the view's axes, and how far it
// rolls the view, in radians, right positive.
struct View {
    Aim::Offset eye;
    float roll;
};

// Leans the pawn's body this frame, given the seconds since the last and whether the player may
// lean now.
View Update(uint8_t* pawn, float seconds, bool free);

// Bends a skeleton freshly posed, if it is the player's.
void OnPosed(uint8_t* skeleton);

void SetEnabled(bool enabled);

}
