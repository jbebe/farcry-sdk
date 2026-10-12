// Every car's lit headlamps, and the one light the car the player drives casts, read as the game
// updates each car's headlights and handed to the render thread with the frame they belong to.
#pragma once

#include <cstdint>

namespace VehicleOverhaul::Headlights {

constexpr int kMaxLamps = 32;

// One lit lamp: where its lens is and which way it faces. `id` tells a lamp from the others across
// frames and is never read through.
struct Lamp {
    uintptr_t id;
    float position[3];
    float forward[3];
};

// The engine's spot light, as CVehicleTypeWheeled::EnableFrontHeadlights sets it up and
// UpdateHeadlights places it: colour already scaled by intensity, cone angles whole and in degrees.
struct Light {
    bool on;
    float position[3];
    float direction[3];
    float range;
    float colour[3];
    float innerAngle;
    float outerAngle;
};

struct Snapshot {
    int lamps;
    Lamp lamp[kMaxLamps];
    Light light;
};

// Hooks the per-car headlight update. False, and logged, when it is missing from this build.
bool Install();

// Seals what was read since the last post as frame `frame`. Main thread, as the frame is posted.
void Post(uint32_t frame);

// The snapshot of frame `frame`. Render thread, while it draws that frame.
const Snapshot& Of(uint32_t frame);

}
