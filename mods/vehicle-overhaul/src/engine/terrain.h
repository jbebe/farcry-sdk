// The terrain's height under a point.
#pragma once

namespace VehicleOverhaul::Terrain {

// False, and logged, when Dunia.dll does not export the height query.
bool Install();

// The ground height in metres at world (x, y). Only while a world is loaded.
float Height(float x, float y);

}
