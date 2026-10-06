// A scope seen from its eyepiece, the same size for every scope: the engine's scope gives way to a
// black, soft body with a mount below it and the scope's own reticle in its opening, laid over the
// finished frame and trailing the look a little as it turns.
#pragma once

#include "engine/weapon_draws.h"

#include <optional>

namespace AimingOverhaul::Eyepiece {

// The opening on screen: its centre off the screen's, x right and y down, its radius, and the
// radius of the body's outer edge around it, in screen heights.
struct Opening {
    float x;
    float y;
    float radius;
    float outer;
};

// While its sight picture is up, drops the draws of the weapon in hand, which its name says the
// scope of.
bool BeforeGunDraw(IDirect3DDevice9* device, const WeaponDraws::Call& call);

// Lays the reticle and the body over the finished frame.
void OnComposite(IDirect3DDevice9* device);

// The opening this frame, once the gun's depth pass has shown a scope drawn from its eyepiece.
std::optional<Opening> Open();

// Whether a scope will likely be drawn from its eyepiece this frame, before the gun's draws say:
// its sight picture is up, or was drawn last frame.
bool Expected();

void SetEnabled(bool enabled);

void ReleaseDeviceObjects();

}
