// The player's look, as the input builds it once a frame, with an offset of the plugin's own on it.
#pragma once

namespace AimingOverhaul::Look {

// Radians.
struct Angles {
    float up;
    float right;
};

// The offset wanted on the look this frame. Only its change since the last frame is added, so the
// view, the gun and the shot all follow it and the mouse still turns from wherever it left them.
using OffsetFn = Angles (*)();

// False, and logged, when the look cannot be hooked.
bool Install(OffsetFn offset);

}
