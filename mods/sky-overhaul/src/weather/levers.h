// One moment's weather as every lever takes it, so two moments can be blended field by field.
#pragma once

#include "weather/table.h"

namespace SkyOverhaul::Weather {

// One fog as the game lays it over its own: where it ends, its tint against the game's grey at that
// hour, how much of it lies on the ground rather than through the whole air, and how far above the
// player's ground that lying part has thinned out.
struct Fog {
    float end;
    float tint[3];
    float low;
    float top;
};

struct Levers {
    float coverage;
    float density;
    float tallness;
    float shade;
    float cirrus;
    float cirrusOpacity;
    float contrails;
    float storminess;
    float haze;
    float heat;

    // How much of the fog is ours, and each of the rest of a Fog multiplied by that share: the game
    // lays our fog over its own by the share, so blending these blends what is seen.
    float fogWeight;
    float fogReach;
    float fogTint[3];
    float fogLow;
    float fogTop;

    float rain;
    float wind;
    float dust;
    float milk;
};

Levers FromRow(const Row& row);

Levers Lerp(const Levers& from, const Levers& to, float t);

// Laid over `base` by `amount`, the nearer of the two fogs winning: mist over a weather.
void AddFog(Levers& base, const Fog& fog, float amount);

// Our fog, an end of nought where it is all the game's.
Fog FogOf(const Levers& levers);

// How much of the sky the clouds cover, the cirrus counting as far as it stops light.
float Cover(const Levers& levers);

}
