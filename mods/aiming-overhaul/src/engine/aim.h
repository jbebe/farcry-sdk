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

// What the plugin adds to the camera this frame.
struct Adjust {
    // The hand's drift, which the eye follows down the iron sights.
    Offset drift;
    // The view's pitch and roll over the body's, in radians, up and right positive.
    float pitch;
    float roll;
};

// Once a frame, with the player's pawn and the seconds since the last, before the camera is placed
// from the body, so what turns the body this frame turns the view too.
using FrameFn = Adjust (*)(uint8_t* pawn, float seconds);

// Follows the weapon in hand, or null: how far the eye comes forward, in metres, as its scope is
// raised; nought unless the plugin draws that scope.
using FollowWeaponFn = float (*)(uint8_t* weapon);

// Hooks the first-person camera; `frame` and `followWeapon` then run once a frame. False, and
// logged, when the camera cannot be hooked.
bool Install(FrameFn frame, FollowWeaponFn followWeapon);

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

// Where the eye was last placed, in the world, or null before it was.
const float* Eye();

// The view's direction, as a unit vector in the world: the body's look this frame, with the view's
// pitch last added over it.
void Forward(float out[3]);

// Whether a scope's magnification comes in at once with its sight picture rather than easing in
// over the raise, so the view outside the eyepiece keeps the field of view it had. The eye then
// comes forward to the scope as it is raised instead.
void SetZoomAtOnce(bool atOnce);

// Whether the view zooms in down a gun's iron sights, as the game does. A scope is not affected.
void SetIronsightZoom(bool enabled);

}
