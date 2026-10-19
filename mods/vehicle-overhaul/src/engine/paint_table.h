// The vehicle paints a car's material takes by its colour index, filled when Dunia.dll loads.
#pragma once

namespace VehicleOverhaul::PaintTable {

// One paint's colours, each RGB and an unused fourth.
struct Entry {
    float base[4];
    float paint[4];
    float dirt[4];
    float specularBase[4];
    float specular[4];
};

// The paint at this index, or null when it is past the table or this build lacks it.
Entry* At(unsigned index);

}
