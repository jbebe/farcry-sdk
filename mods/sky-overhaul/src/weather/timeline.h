// The weather at any moment of a playthrough, worked out from the game's clock alone: an air mass
// over the whole world and the cells drifting through it. The same moment always gives the same
// weather, so a save carries it without storing it.
#pragma once

#include "weather/cells.h"
#include "weather/levers.h"

namespace SkyOverhaul::Weather {

struct Moment {
    // Game clock since the playthrough's first midnight, counted days included.
    double seconds;
    // The sine of the sun's elevation.
    float sunHeight;
    // The zones' shares under the player, as the game blends them.
    float savannah;
    float jungle;
    float desert;
    // Where the player stands, and the world: 1 or 2, nought while not yet known.
    float x;
    float y;
    int world;
    Planted planted;
};

struct Reading {
    // The weather at the player, and the air mass alone, which the sky away from the cells shows.
    Levers levers;
    Levers sky;
    // The row showing most, the one it is blending toward (itself when none), how far toward it,
    // and game seconds until the next row is wholly in.
    int row;
    int next;
    float blend;
    double secondsLeft;
    // Morning fog and mist after rain, as far as either has come.
    float mist;
    // One just after rain, nought once the ground has dried.
    float wet;
    // How much dust is in the air, from nought to thick.
    float dust;
    // The game's convention: degrees, the way the wind blows toward.
    float windDegrees;
    // How much of the weather at the player is the cells', and metres to the nearest cell's centre,
    // below nought for none near.
    float cell;
    float cellDistance;
    // How full the puddles are: nought dry, one brimming.
    float puddles = 0.0f;
    // How far a rainbow can stand in the shower moving off, given sun: coming up once the rain here
    // stops and gone half an hour later.
    float bow = 0.0f;
};

Reading Read(const Moment& moment);

// One row held, with nothing of the timeline in it but a planted cell: no mist, and rain's, dust's
// and the ground's part as the row alone gives them.
Reading Hold(int row, const Moment& moment);

// The cells over the whole map at a moment, as many as `capacity` holds, nearest the player first.
int MapCells(const Moment& moment, bool rolled, Cell* out, int capacity);

// Clock seconds for the wind to turn once round.
inline constexpr double kWindTurn = 3.0 * 86400.0;

// The clouds' drift round the circle the turning wind traces, in radians.
double DriftAngle(double seconds);

// How far something drifting at `speed` metres a clock second has gone by `seconds`, round that
// circle.
void Drift(double seconds, double speed, double out[2]);

// How readily the air mass at `seconds` lets cells grow, against one for clear air.
float Instability(double seconds);

}
