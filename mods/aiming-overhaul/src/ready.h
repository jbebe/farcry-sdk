// The gun carried low in a sprint and brought up after it, slower the heavier it is: until it is up
// it neither fires nor comes up to the sights.
#pragma once

#include "body.h"

namespace AimingOverhaul::Ready {

// Once a frame, with whether the player sprints and the gun's weight in kilograms.
void Update(float seconds, bool sprinting, float kilograms);

// Whether the gun is still on its way up, refusing shots and sights.
bool Lowered();

// How far the arms hold the gun low this frame.
Body::Offset Posed();

void SetEnabled(bool enabled);

}
