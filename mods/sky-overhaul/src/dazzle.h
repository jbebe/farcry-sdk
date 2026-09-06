// The sun, as something the eye cannot look at.
#pragma once

namespace SkyOverhaul::Dazzle {

// Follows the frame and takes over the finished image. Call once from FCSE_Load. False means the
// frame cannot be followed, which it logs, and nothing is left hooked.
bool Install();

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

}
