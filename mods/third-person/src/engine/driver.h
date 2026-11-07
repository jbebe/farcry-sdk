// Whether the player is at the wheel, from the game giving each car its driver.
#pragma once

namespace ThirdPerson::Driver {

// False, and logged, when this build lacks the call that gives a car its driver.
bool Install();

// Whether the player drives a car. Any thread.
bool Driving();

}
