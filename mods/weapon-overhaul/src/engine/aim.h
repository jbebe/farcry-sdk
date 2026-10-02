// The player's aim, as the first-person camera sees it once a frame, and the eye's place off the gun.
#pragma once

namespace WeaponOverhaul::Aim {

// Metres along the view's own axes.
struct Offset {
    float right;
    float up;
};

// The hand's drift this frame, given the seconds since the last.
using DriftFn = Offset (*)(float seconds);

// Hooks the first-person camera and the look; `drift` then runs once a frame. Down the iron sights
// the eye follows it, and through a scope the aim turns with it. False, and logged, when the
// camera cannot be hooked.
bool Install(DriftFn drift);

// How far the eye has settled into the iron sights, and into a scope's own sight picture, eased
// from nought to one. At most one of them is above nought.
float Settled();
float Scoped();

// The hand's drift last frame.
Offset Drift();

}
