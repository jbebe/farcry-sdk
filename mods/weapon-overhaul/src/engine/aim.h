// The player's aim, as the first-person camera sees it once a frame, and the eye's place off the gun.
#pragma once

namespace WeaponOverhaul::Aim {

struct Frame {
    float seconds;
    // How far the eye has settled into the iron sights, eased from nought to one. Nought while a
    // scope's own sight picture is up, which is drawn in place of the gun.
    float settled;
};

// Metres along the view's own axes.
struct Offset {
    float right;
    float up;
};

using EyeFn = Offset (*)(const Frame& frame);

// Hooks the first-person camera; `eye` then runs once a frame and its answer moves the eye that far
// from where the engine put it. False, and logged, when the camera cannot be hooked.
bool Install(EyeFn eye);

// The last frame's settle, for whatever draws after it.
float Settled();

}
