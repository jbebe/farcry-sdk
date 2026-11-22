// The weathers: every row of docs/docs/design/weather.md as the values its levers take.
#pragma once

#include <cstddef>

namespace SkyOverhaul::Weather {

enum class Family { Clear, Foggy, Overcast, Storm, Wind };

struct Row {
    const char* id;
    const char* name;
    Family family;

    // The cumulus layer: how much of the sky it fills, how solid it is, how tall it grows from
    // nought (flat) through a half (as tuned) to one (towering), and how dark its base is.
    float coverage;
    float density;
    float tallness;
    float shade;

    float cirrus;
    float cirrusOpacity;
    float contrails;

    // The air's storm look from nought to one: the sun dimmed, the sky greyed and hazed, and the
    // game's storm lighting with them.
    float storminess;
    // The air's haze against what that storminess alone gives.
    float haze;
    // How much of the sun's heat haze the ground gives off.
    float heat;

    // Metres to the fog's end, or nought for the game's own fog. The tint keeps the game's brightness
    // at that hour: (1, 1, 1) is a grey of it. Rain, dust and mist lay fogs of their own over it.
    float fog;
    float fogTint[3];

    float rain;
    // The game's wind force, 0 to 250.
    float wind;

    // The least dust this weather keeps in the air on dry ground, whatever the day's own.
    float dust;
    // How milky haze through the whole air makes the sky, at least.
    float milk;
};

// Every row, in the order the design log lists them. Rows are mutable so a tuned value can stand
// in for its default.
Row* Rows();
size_t RowCount();

// The row's index, or -1 for no such id.
int Find(const char* id);

}
