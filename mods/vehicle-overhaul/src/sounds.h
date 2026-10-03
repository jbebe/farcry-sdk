// Every sound of the car the player drives, each to switch off and on from the window.
#pragma once

namespace VehicleOverhaul::Sounds {

// False, and logged, when this build lacks the sound system, the vehicle's sound update or the input
// pass.
bool Install();

// The window's Sounds tab.
void DrawTab();

}
