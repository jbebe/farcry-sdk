// The arms slide and point behind the look as it turns, sideways and up and down, the slide twice as
// far as the game's own, and come back as slowly as the gun is heavy, slower still after a hard
// twitch. Down the sights the gun turns about its own middle instead, its muzzle leading the turn.
// The scope's eyepiece lags by the same turn. See
// docs/docs/engine-internals/first-person-aiming.md#the-arms-behind-a-turn.
#pragma once

#include "body.h"
#include "engine/aim.h"

namespace AimingOverhaul::Inertia {

// Takes the game's own slide away while this is on; false where it cannot.
bool Install();

// Once a frame on the game thread, after the look's turn is measured, with how far the player has
// settled into the sights or a scope, from nought to one.
void Update(float seconds, float aimed);

// How far the arms slide this frame; none while switched off.
Body::Offset Posed();

// The turn the arms and the scope lag behind, eased by the gun's weight, in radians a second.
Aim::Turning Lagged();

void SetEnabled(bool enabled);

}
