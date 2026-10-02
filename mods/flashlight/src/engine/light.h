// One of the engine's own scene lights, owned the way the player's vehicle owns its headlight, and
// the camera it is placed from.
#pragma once

#include <optional>

namespace Flashlight::Light {

struct Spot {
    float range;
    float outerAngle;
    float innerAngle;
    const float* colour;
    bool castShadow;
    float shadowFactor;
};

// False, and logged, when this build lacks the light or camera calls.
bool Install();

// Writes everything about the light but where it is, creating it first if there is none.
void Configure(const Spot& spot);

void SetEnabled(bool enabled);

struct Pose {
    float position[3];
    float direction[3];
};

// Places the light at the player's camera, `above` and `right` of the eye in metres, aimed where
// the camera looks, at `intensity`, and returns where. Leaves it where it was, and returns nothing,
// when there is no camera to read.
std::optional<Pose> Place(void* player, float intensity, float above, float right);

// The light belongs to the world it was made in; call this when that world goes.
void Destroy();

}
