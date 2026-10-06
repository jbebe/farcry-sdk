// The player's aim, as the first-person camera sees it once a frame, and the eye's place off the gun.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Aim {

// Metres along the view's own axes.
struct Offset {
    float right;
    float up;
};

// A share of the scope's opening radius, x right and y down.
struct Swing {
    float x;
    float y;
};

// The hand's drift this frame, given the seconds since the last.
using DriftFn = Offset (*)(float seconds);

// How far the eye comes forward, in metres, as the scope of the weapon with this entity name is
// raised; nought unless the plugin draws that scope.
using RaiseReachFn = float (*)(const char* weaponName);

// Hooks the first-person camera; `drift` then runs once a frame, and down the iron sights the eye
// follows it, and `raiseReach` each time the weapon in hand changes. False, and logged, when the
// camera cannot be hooked.
bool Install(DriftFn drift, RaiseReachFn raiseReach);

// How far the eye has settled into the iron sights, and into a scope's own sight picture, eased
// from nought to one. At most one of them is above nought.
float Settled();
float Scoped();

// Whether a scope's own sight picture is up in the game's frame, which the engine shows at once
// and the render thread can draw a frame or more later.
bool ScopeUp();

// Whether the player is down the iron sights or a scope at all.
bool Aiming();

// How far the scope's shadow has swung off the opening's centre, against the look's turn.
Swing ScopeSwing();

// How many times the scope last raised magnifies the view.
float Magnification();

// How far a scope has kicked back toward the eye from the last shot, from nought to one.
float ScopeKick();

// The name of the weapon in hand's entity, empty when there is none, and how many times the weapon
// in hand has changed, which says when to read the name again.
const char* WeaponName();
uint32_t WeaponChanges();

// Whether a scope's magnification comes in at once with its sight picture rather than easing in
// over the raise, so the view outside the eyepiece keeps the field of view it had. The eye then
// comes forward to the scope as it is raised instead.
void SetZoomAtOnce(bool atOnce);

}
