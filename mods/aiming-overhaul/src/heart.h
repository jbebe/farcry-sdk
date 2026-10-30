// The heart: its rate climbs with exertion and comes back down as the stamina does, jumps with a
// wound or a blast nearby and settles slowly from that; each beat jolts the aim, and a fast heart
// steadies the hands less.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Heart {

// Once a frame, with the seconds since the last, whether the player sprints, and how much stamina
// and health they have left and how hard the camera is shaken, each as the pawn reads them.
void Update(float seconds, bool sprinting, float stamina, float health, float shake);

// How fast the heart runs, from nought at rest to one at its fastest.
float Racing();

// Whether the heart beat in the frame Update last moved.
bool Beat();

// How far the last beat has jolted the aim now, from nought to one.
float Jolt();

}
