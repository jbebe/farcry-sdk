// The sun, as something the eye cannot look at.
#pragma once

#include "engine/frame.h"

namespace SkyOverhaul::Dazzle {

// Prepares the effect. Call once from FCSE_Load.
void Install();

// Measures the sun against the world's depth on the sky pass, and paints the glare over the
// composite.
void OnScenePass(const Frame::Pass& pass);
void OnFinalPass(const Frame::Pass& pass);

// Frees everything held on the device. Call before the engine resets it.
void ReleaseDeviceObjects();

// Peak brightness with the sun dead centre, as a percentage. Zero turns the glare off entirely.
void SetStrength(int percent);

// How far off centre the sun gets, in degrees, before the glare reaches nothing.
void SetSpread(int degrees);

// How sharply the glare falls away from dead centre, in tenths. Higher means full strength is
// reserved for looking straight at the sun.
void SetFalloff(int tenths);

// How hard the contrast is pushed at full dazzle, as a percentage.
void SetContrast(int percent);

// How fast colour drains as the dazzle rises, as a percentage. A hundred reaches fully grey only
// at full dazzle; above that it gets there sooner.
void SetDesaturation(int percent);

// How much of the glare covers the frame wherever the sun happens to sit in it, as a percentage.
// This is what keeps the brightness tied to the angle to the sun rather than to the sun's place on
// screen.
void SetVeil(int percent);

// The sun's elevation, in degrees, at which it reaches full strength. Below that it weakens, so a
// setting sun dazzles less than a midday one.
void SetElevationRamp(int degrees);

// How strongly the burned-in view shows once the player looks away, as a percentage.
void SetAfterimageStrength(int percent);

// The longest the eye's exposure can build up to, in seconds, which is also how long the
// afterimage takes to fade after staring that long.
void SetAfterimageSeconds(int seconds);

// How darkly the bleached core blocks the view at its peak, as a percentage.
void SetAfterimageDarkness(int percent);

// How strongly the faint negative surround shows, as a percentage. Far weaker than the core: an
// obvious bright halo is what makes an afterimage look painted on.
void SetAfterimageTint(int percent);

// How much of that negative's colour survives, as a percentage. Nought inverts only the light and
// leaves it grey; a hundred gives the full complementary colours of a photographic negative.
void SetAfterimageSaturation(int percent);

// How far the eye's range compresses while it recovers, as a percentage. It drains a little colour
// and contrast from the whole picture, not just from the blocked part.
void SetAfterimageHaze(int percent);

// How wide the bleached region is, as a percentage of the glare's own reach. The eye burns where
// the sun's image falls, which is far tighter than everything the glare washes.
void SetAfterimageSize(int percent);

}
