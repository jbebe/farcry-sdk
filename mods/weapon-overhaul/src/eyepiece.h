// A scope seen from its eyepiece, the same size for every scope: the engine's scope gives way to a
// black, soft body with a mount below it and the scope's own reticle in its opening, laid over the
// finished frame and trailing the look a little as it turns.
#pragma once

#include "engine/weapon_draws.h"

#include <optional>

namespace WeaponOverhaul::Eyepiece {

// The opening on screen: its centre off the screen's, x right and y down, and its radius, in screen
// heights; and the body's black rim around it, as a share of its radius.
struct Opening {
    float x;
    float y;
    float radius;
    float rim;
};

// Drops the engine's scope once its housing's draw says which scope it is.
bool BeforeGunDraw(IDirect3DDevice9* device, const WeaponDraws::Call& call);

// Lays the reticle and the body over the finished frame.
void OnComposite(IDirect3DDevice9* device);

// The opening this frame, while a scope is drawn from its eyepiece.
std::optional<Opening> Open();

void SetEnabled(bool enabled);

void ReleaseDeviceObjects();

}
