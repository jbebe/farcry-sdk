// The arms slide down behind the look as it pitches up, and up as it pitches down, as the game's own
// animation slides them sideways behind a turn and never up or down: the same rate, smoothing and
// size, and none down the sights. See
// docs/docs/engine-internals/first-person-aiming.md#the-arms-behind-a-turn.
#pragma once

#include "body.h"

namespace AimingOverhaul::PitchSlide {

// Once a frame on the game thread, after the look's turn is measured, with how far the player has
// settled into the sights or a scope, from nought to one.
void Update(float seconds, float aimed);

// How far the arms slide this frame; none while switched off.
Body::Offset Posed();

void SetEnabled(bool enabled);

}
