// The sun's glare, as a wash over the finished frame.
#pragma once

namespace SkyOverhaul::Veil {

// Takes over the device's Present and Reset. Call once from FCSE_Load. False means the wash cannot
// be drawn, which it logs, and nothing is left hooked.
bool Install();

// Peak brightness with the sun dead centre, as a percentage. Zero turns the wash off entirely.
void SetStrength(int percent);

// How far off centre the sun gets, in degrees, before the wash reaches nothing.
void SetSpread(int degrees);

}
