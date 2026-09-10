// The engine's own cloud layer, followed at the one function that submits it.
//
// That submission reads every parameter the shipped clouds are lit by out of the renderer's scene
// state, so following it is also how a replacement reaches a lighting environment that already
// agrees with the sky dome. See docs/docs/engine-internals/sky-and-clouds.md.
#pragma once

#include <cstdint>

namespace SkyOverhaul::CloudLayer {

// Whether the engine draws its own clouds. Suppressing them is the same thing the engine does for
// a world whose cloud layers are both disabled, and it takes the god-ray cloud mask with it.
enum class Mode {
    Engine,
    Off,
};

// The cloud lighting for one frame, as the environment manager left it: the world's presets
// already blended by time of day and by storm, so these are finished values rather than curves.
struct Lighting {
    // Unit length, Z up, pointing at the light.
    float sunDirection[3];
    float moonDirection[3];

    float sunColour[3];
    float moonColour[3];
    float ambientColour[3];
    float backSunColour[3];

    // How far the lower layer has scrolled, whose direction is the wind's.
    float wind[2];

    // Zero in clear weather, one in a full storm.
    float storm;
    // Zero in daylight, one at night.
    float night;
};

// Hooks the cloud-layer submission. Call once from FCSE_Load. False means the submission was not
// found, which it logs, and nothing is left hooked.
bool Install();

// The newest complete snapshot. False until the engine has submitted a cloud layer.
bool Latest(Lighting& out);

// Counts cloud-layer submissions, suppressed ones included. It runs at every hour, so a render
// frame whose count differs from the previous one had a world drawn into it.
uint32_t SubmitCount();

// Takes effect on the next submission. Safe to call before anything has been submitted.
void SetMode(Mode mode);

}
