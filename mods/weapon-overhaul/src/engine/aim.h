// The player's aim, as the first-person camera sees it once a frame, and the eye's place off the gun.
#pragma once

namespace WeaponOverhaul::Aim {

struct Frame {
    float seconds;
    // Down the iron sights or a scope.
    bool sights;
    // A scope's own sight picture is up, drawn in place of the gun.
    bool scope;
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

}
