// The same air the sky shader looks through, on the processor.
//
// The horizon a player sees is half sky and half land, and the land is coloured by fog constants
// the engine hands to every shader in the frame. For the two halves to meet, something outside the
// sky shader has to know what colour the sky arrives at - so this integrates the same scattering,
// for one direction at a time, a few times a frame.
//
// It is a hand-kept copy of `Scattered` in src/shaders/sky.fx, constant for constant. They colour
// two halves of one horizon, so a change to either belongs in both.
#pragma once

namespace SkyOverhaul::SkyModel {

// What one look along `ray` comes back with, given a sun direction, the eye's height in metres,
// how much haze the air carries and how bright the sun is. Both directions are unit length, Z up.
void Radiance(const float ray[3], const float sun[3], float eyeHeight, float mie, float intensity,
              float out[3]);

}
