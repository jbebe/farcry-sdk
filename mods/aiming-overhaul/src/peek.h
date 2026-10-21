// Auto-lean: aiming with a wall close ahead leans out to the first side that is clear past it, just
// far enough to see.
#pragma once

namespace AimingOverhaul::Peek {

// Once a frame, with whether the aim key is held, the eye as placed, or null when it is not known,
// the view's direction and the roll now: the roll to lean to, in radians, right positive, nought
// for none.
float Update(float seconds, bool aimHeld, const float eye[3], const float forward[3], float roll);

void SetEnabled(bool enabled);

}
