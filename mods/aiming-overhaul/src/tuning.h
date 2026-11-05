// How each weapon's shots shake the view, how much of the sway is left down the sights, and how dirt
// in the eye and being hit look, kept in bin\aiming-overhaul.ini and edited in Aiming Overhaul's
// window in DevTools' overlay.
#pragma once

#include <cstdint>

namespace AimingOverhaul::Tuning {

// Degrees for each newton-second of a round's push, and seconds.
struct Values {
    float kickPerImpulse = 0.149f;
    float kickTime = 0.4f;
    float kickDelay = 0.1f;
    float rollPerImpulse = 0.103f;
    float rollCycle = 0.08f;
    float rollFade = 0.11f;
};

// How much of the sway down the sights is left, standing and crouched, the same for every weapon.
struct SightSway {
    float standing = 0.5f;
    float crouched = 0.5f;
};

const SightSway& Sights();

// How dirt in the eye looks and how near a round has to land to throw it, the same for every
// weapon.
struct DirtLook {
    float size = 0.315f;
    float darkness = 0.55f;
    float blur = 10.0f;
    float clear = 3.0f;
    float range = 3.0f;
};

const DirtLook& DirtInTheEye();

// How a hit on the player looks, the same for every weapon: the punch a body hit gives the view, in
// metres, degrees and seconds; the vignette after it; and a head hit's black and double image.
struct HitLook {
    float back = 0.05f;
    float down = 3.0f;
    float peak = 0.07f;
    float vignette = 0.85f;
    float reach = 0.55f;
    float vignetteGone = 2.5f;
    float black = 0.35f;
    float unblack = 1.5f;
    float apart = 40.0f;
    float doubleGone = 5.0f;
};

const HitLook& BeingHit();

// Follows the weapon in hand, or null, taking its values each time it changes.
void Follow(uint8_t* weapon);

// The weapon in hand's values, which the window edits in place.
const Values& InHand();

// Reads bin\aiming-overhaul.ini over the defaults and writes it back complete.
void Load();

// The window's contents, for DevTools' overlay.
void DrawWindow(void*);

}
