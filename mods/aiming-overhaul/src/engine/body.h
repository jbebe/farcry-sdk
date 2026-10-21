// The player's body, leaned about the belly once the physics has placed it each frame, so the arms,
// the gun, the eye and where a shot leaves from are all posed from the leaned body.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Body {

// Metres from the feet up to the belly the body leans about.
inline constexpr float kPivotHeight = 0.65f;

// False, and logged, when the physics post-step cannot be hooked.
bool Install();

// Leans `pawn` by `roll` radians, right positive, from the next physics post-step on. A pawn not
// set again for a few post-steps is stood upright and let go.
void Lean(uint8_t* pawn, float roll);

}
