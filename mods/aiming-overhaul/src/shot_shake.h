// The view shaken by the player's shots, as hard as the round pushes: a kick up that springs back,
// which through a burst of full-auto fire fades into a roll clockwise and back. Only the camera
// turns; the gun and the shot stay where they are.
#pragma once

#include "engine/kick.h"
#include "tuning.h"

#include <cstdint>
#include <span>

namespace AimingOverhaul::ShotShake {

// The view's turn off the look, in radians, pitched up and rolled right.
struct Turn {
    float pitch;
    float roll;
};

// Follows the weapon in hand, or null, reading whether it fires full auto each time it changes.
void FollowWeapon(uint8_t* weapon);

// Shakes the view for a shot: by its round's push, or where the gun names no round, by the game's
// kick for the gun, its wear and the sights.
void Kick(const Kick::Kick& kick);

// Once a frame, with the seconds since the last: the view's turn this frame, none while a scope's
// sight picture is up.
Turn Update(float seconds);

// The window's sliders for the shake, and what it shows under them: the weapon in hand's round and
// what the last shot shook.
std::span<const Tuning::Slider> Sliders();
void DrawLastShot();

void SetEnabled(bool enabled);

}
