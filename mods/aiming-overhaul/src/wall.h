// The gun near walls: a gun that would go into what is ahead swings down out of it, and while it is
// down it neither fires nor comes up to the sights.
#pragma once

#include "body.h"

namespace AimingOverhaul::Wall {

// Once a frame, from the eye, or null when it is not known, and the view's direction.
void Update(float seconds, const float eye[3], const float forward[3]);

// How far the arms hold the gun swung down this frame.
Body::Offset Posed();

// Turns the direction of a shot about to leave, a unit vector in the world, down as far as the gun
// is swung, so it goes where the gun points.
void Bend(float direction[3]);

// Whether the gun is down far enough that it neither fires nor aims.
bool Blocked();

void SetEnabled(bool enabled);

}
