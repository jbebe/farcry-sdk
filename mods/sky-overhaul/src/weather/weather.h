// The weather the sky and the game's own storm, rain, wind and fog follow, read once a frame from
// the game's clock. See docs/docs/design/weather.md.
#pragma once

#include "weather/timeline.h"

#include <cstdint>

namespace SkyOverhaul::Weather {

// From fcse.ini's Weather mode. Off leaves the game's storm curve, rain roll, wind and fog alone.
void SetEnabled(bool enabled);

// Runs on the game's thread from the environment manager's frame, with the manager.
void Update(const uint8_t* manager);

// This frame's weather on the game's thread, for the hooks that hand it to the engine. False while
// the weather is off.
bool ForEngine(Reading& out);

// The newest frame's weather, from any thread. False while off or before a world frame.
bool Latest(Reading& out);

// The game clock as the last world frame left it, from any thread. False before one.
bool Clock(double& seconds);

// Holds one row in place of the timeline, or lets go with -1.
void Force(int row);
int Forced();

// Puts a storm down `distance` metres from the camera toward `degrees` (the game's convention), at
// its full from the next frame; a distance below nought takes it away.
void Plant(float degrees, float distance);

// The row on screen: the held one, else the one showing most. -1 while the weather is off.
int Showing();

// The camera in the world and the way it looks, kept from the frame for the fog laid on the ground
// around it, the cells near it and a storm planted ahead. Eye and Facing are false before the first
// frame; Facing gives degrees as the wind's are given.
void SetEye(const float eye[3], const float direction[3]);
bool Eye(float out[3]);
bool Facing(float& degrees);

// The cells over the map as the newest frame left them, for the sky to draw, with the way they
// drift. Count is nought while the weather is off.
inline constexpr int kMapCells = 96;
struct MapCellList {
    int count;
    float heading[2];
    Cell cells[kMapCells];
};
bool LatestCells(MapCellList& out);

// What the sky draws with: the weather's air while it is on, the game's storm factor's otherwise.
struct Air {
    bool weather;
    // Only while `weather`: the air mass's levers, which the cloud layer takes away from the cells.
    Levers sky;
    float storminess;
    // The haze the air carries, how dark the cloud base is, the share of the heat haze left, how
    // milky haze through the whole air makes the sky, and how far the weather's fog swallows the
    // land before the horizon.
    float haze;
    float shade;
    float heat;
    float milk;
    float veil;
    // Only while `weather`: how much of the moon's light the clouds over the player keep off the
    // ground.
    float cover;
};
Air AirNow(float engineStorm);

}
