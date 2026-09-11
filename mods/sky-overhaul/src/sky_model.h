// The same air the sky shader looks through, on the processor: the sky's colour along one ray at a
// time, for the land's fog and the zenith's lift, and the sunlight that reaches the clouds.
//
// It is a hand-kept copy of `Scattered` in src/shaders/sky.fx, constant for constant, so a change to
// either belongs in both.
#pragma once

namespace SkyOverhaul::SkyModel {

// How far a storm factor has risen past fair weather, from nought to one in a full storm.
float Storminess(float storm);

// How much haze the air carries at that storminess.
float Haze(float storminess);

// The share of the sun's light that reaches the air at that storminess.
float SunShare(float storminess);

// How far the sky's colour is drained toward grey at that storminess.
float Grey(float storminess);

// What one look along `ray` comes back with, given a sun direction, the eye's height in metres,
// how much haze the air carries and how bright the sun is. Both directions are unit length, Z up.
void Radiance(const float ray[3], const float sun[3], float eyeHeight, float mie, float intensity,
              float out[3]);

// The share of each colour of sunlight that reaches a point `height` metres up through air carrying
// `mie` haze: nothing where the planet stands between the point and the sun.
void Sunlight(const float sun[3], float height, float mie, float out[3]);

}
