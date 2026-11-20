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
    // How far full auto's shake points the arms off at its most, in times the gun's own kick.
    float shake = 1.5f;
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
    float size = 0.5f;
    float darkness = 0.7f;
    float blur = 10.0f;
    float clear = 3.0f;
    float range = 3.0f;
};

const DirtLook& DirtInTheEye();

// How a hit on the player looks, the same for every weapon: the punch it gives the view, in metres,
// degrees and seconds; the vignette and the whole view's dimming after it, which a head hit gives in
// full; and a head hit's double image.
struct HitLook {
    float back = 0.08f;
    float down = 5.0f;
    float peak = 0.07f;
    float vignette = 0.98f;
    float reach = 0.46f;
    float vignetteGone = 1.3f;
    float dim = 0.35f;
    float dimGone = 1.0f;
    float apart = 40.0f;
    float doubleGone = 5.0f;
};

const HitLook& BeingHit();

// How the picture shows the player's health, the same for every weapon: the colour left at 3 bars,
// 2 bars and the last bar, nought to one, the grain and glow at the last two, and the seconds from
// one to the next.
struct HealthLook {
    float colour3 = 0.67f;
    float colour2 = 0.33f;
    float grain2 = 0.04f;
    float glow2 = 0.2f;
    float colour1 = 0.0f;
    float grain1 = 0.12f;
    float glow1 = 0.5f;
    float fade = 1.0f;
};

const HealthLook& HealthPicture();

// Takes the values of the weapon now in hand, by its name. Call each time the weapon changes.
void Follow();

// The weapon in hand's values, which the window edits in place.
const Values& InHand();

// Reads bin\aiming-overhaul.ini over the defaults and writes it back complete.
void Load();

// The window's contents, for DevTools' overlay.
void DrawWindow(void*);

}
