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

// Follows the weapon in hand, or null: how far the eye comes forward, in metres, as its scope is
// raised; nought unless the plugin draws that scope.
using FollowWeaponFn = float (*)(uint8_t* weapon);

// The body's roll this frame in radians, right positive, given the seconds since the last and
// whether the player may lean: on foot and not sprinting.
using LeanFn = float (*)(float seconds, bool free);

// Hooks the first-person camera; `drift`, `followWeapon` and `lean` then run once a frame, down the
// iron sights the eye follows the drift, and the body rolls by the lean before the camera is placed
// from it. False, and logged, when the camera cannot be hooked.
bool Install(DriftFn drift, FollowWeaponFn followWeapon, LeanFn lean);

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

// The player's pawn as the camera last saw it, and whether that was a moment ago, on foot.
uint8_t* Pawn();
bool OnFoot();

// Where the eye was last placed, in the world, or null before it was.
const float* Eye();

// The view's direction this frame, as a unit vector in the world.
void Forward(float out[3]);

// Swings the gun down by `radians` while the view holds still: the camera is raised by as much as
// the look is lowered, which the look's offset does with GunDown(). While `blocked`, the sights
// are let go.
void SetGunDown(float radians, bool blocked);
float GunDown();

// Whether a scope's magnification comes in at once with its sight picture rather than easing in
// over the raise, so the view outside the eyepiece keeps the field of view it had. The eye then
// comes forward to the scope as it is raised instead.
void SetZoomAtOnce(bool atOnce);

// Whether the view zooms in down a gun's iron sights, as the game does. A scope is not affected.
void SetIronsightZoom(bool enabled);

}
