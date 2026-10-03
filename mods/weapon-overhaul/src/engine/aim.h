// The player's aim, as the first-person camera sees it once a frame, and the eye's place off the gun.
#pragma once

namespace WeaponOverhaul::Aim {

// Metres along the view's own axes.
struct Offset {
    float right;
    float up;
};

// Radians a second.
struct Turn {
    float right;
    float up;
};

// The hand's drift this frame, given the seconds since the last.
using DriftFn = Offset (*)(float seconds);

// Hooks the first-person camera; `drift` then runs once a frame, and down the iron sights the eye
// follows it. False, and logged, when the camera cannot be hooked.
bool Install(DriftFn drift);

// How far the eye has settled into the iron sights, and into a scope's own sight picture, eased
// from nought to one. At most one of them is above nought.
float Settled();
float Scoped();

// How fast the look turns, smoothed the way the gun trails it.
Turn Turning();

}
