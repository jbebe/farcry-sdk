// The light the world's shadows are cast by, read where the engine submits its cloud layer, which is
// handed the renderer's scene state every frame a world is drawn. See
// docs/docs/engine-internals/sky-and-clouds.md and time-of-day-and-lighting.md.
#pragma once

#include <cstdint>

namespace PlayerBody::Sun {

struct Light {
    // Unit length, z up, toward the light, held no flatter than the engine lays its shadows.
    float direction[3];
    // Each 0 to 1: a full storm, full night.
    float storm;
    float night;
};

// Hooks the cloud-layer submission. False, and logged, when this build lacks it.
bool Install();

// False until the engine has submitted a cloud layer. Any thread.
bool Latest(Light& out);

// How many cloud layers have been submitted; one more each frame a world is drawn. Any thread.
uint32_t Submissions();

}
