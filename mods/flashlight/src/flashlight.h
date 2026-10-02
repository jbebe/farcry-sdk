// The flashlight: a spot light at the player's head, switched by the Flashlight control, with a
// click and a HUD icon.
#pragma once

#include <cstddef>

namespace Flashlight {

// The cone sizes the Cone setting picks between, in its order.
inline constexpr const char* kConeLabels[] = {"Small", "Medium", "Large"};
inline constexpr size_t kDefaultCone = 1;

// Finds what it needs and hooks the frame and the dispatcher. False, and logged, when this build is
// missing anything the light itself needs; the click, the icon and the guards seeing it are optional.
bool Install();

// Both take effect on the next frame, on a light that is on as well as the next one switched on.
void SetCone(size_t cone);
void SetShadows(bool shadows);

}
