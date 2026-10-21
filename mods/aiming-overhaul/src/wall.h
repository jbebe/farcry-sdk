// The gun near walls: a gun that would go into what is ahead swings down out of it, and while it is
// down it neither fires nor comes up to the sights.
#pragma once

namespace AimingOverhaul::Wall {

// Once a frame, from the eye and the view's direction: how far the gun is swung down, in radians.
float Update(float seconds, const float eye[3], const float forward[3]);

// Whether the gun is down far enough that it neither fires nor aims.
bool Blocked();

void SetEnabled(bool enabled);

}
