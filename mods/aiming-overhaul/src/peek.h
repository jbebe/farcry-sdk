// Auto-lean: aiming with a wall close ahead leans out to the first side that is clear past it, just
// far enough to see.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Peek {

// Follows the aim signals.
void OnSignal(uint32_t signal);

// Once a frame, from the eye as placed, the view's direction and the body's roll now: the roll to
// lean to, in radians, right positive, nought for none.
float Update(float seconds, const float eye[3], const float forward[3], float roll);

// Where the eye goes, in the world, when the body rolls from `from` to `to` radians about its feet,
// facing `forward`.
void Shift(const float forward[3], float from, float to, float out[3]);

void SetEnabled(bool enabled);

}
