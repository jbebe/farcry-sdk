// The player's aim, as the first-person camera sees it once a frame, and the eye's place off the gun.
#pragma once

#include <cstdint>

namespace WeaponOverhaul::Aim {

// Metres along the view's own axes.
struct Offset {
    float right;
    float up;
    float ahead;
};

// A share of the scope's opening radius, x right and y down.
struct Swing {
    float x;
    float y;
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

// Whether a scope's own sight picture is up this frame, which the engine shows at once, and how many
// times one has come up.
bool ScopeUp();
uint32_t ScopeUps();

// How far the scope's shadow has swung off the opening's centre, against the look's turn.
Swing ScopeSwing();

// Whether a scope's magnification comes in at once with its sight picture rather than easing in
// over the raise, so the view outside the eyepiece keeps the field of view it had. The eye then
// comes forward to the scope as it is raised instead, by as many metres as SetRaiseReach gives.
void SetZoomAtOnce(bool atOnce);
void SetRaiseReach(float metres);

}
