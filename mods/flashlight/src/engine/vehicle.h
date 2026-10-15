// Whether the player sits in a vehicle.
#pragma once

namespace Flashlight::Vehicle {

// Logged when this build lacks the input pass or the seat lookup.
void Install();

// As of the player's last input pass; false until there has been one.
bool Seated();

}
