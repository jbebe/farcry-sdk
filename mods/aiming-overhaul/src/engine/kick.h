// The kick each shot gives a pawn's aim, which the game takes back once the shots stop. A kick can
// be taken from the game instead, which then neither raises the aim by it nor takes it back.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Kick {

// Radians, up and right, and the seconds the game would take to reach it.
struct Kick {
    float up;
    float right;
    float seconds;
};

// Whether the game gives up `pawn`'s new kick.
using TakeFn = bool (*)(uint8_t* pawn, const Kick& kick);

// False, and logged, when the recoil cannot be hooked.
bool Install(TakeFn take);

}
