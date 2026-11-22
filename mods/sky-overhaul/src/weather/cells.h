// Shower and storm cells: each gathers, rains and dies away over a few game hours while it drifts
// with the weather's wind, rolled from the game's clock alone like the air mass.
#pragma once

namespace SkyOverhaul::Weather {

// A storm put down by hand, DevTools' way of setting one up for a picture.
struct Planted {
    float x;
    float y;
    // Clock seconds it was put down at, below nought for none.
    double at;
};

struct Cell {
    // Its centre in world metres, and how far out its cloud reaches.
    float x;
    float y;
    float radius;
    // From nought as it starts up to one at its full, and back down as it dies away.
    float strength;
    // From nought while it only gathers to one once it rains and spreads its anvil.
    float mature;
    // How far ahead of its centre it is still sure to arrive at its full, which is as far as its
    // storm warning reaches; below nought once it dies away.
    float reach;
    // How far behind its centre lies ground it has rained on, and how much that ground still shows
    // the clearing.
    float wake;
    float clearing;
    // Whether its heart is a heavy storm.
    bool strong;
};

struct CellQuery {
    double seconds;
    int world;
    Planted planted;
    // False leaves only the planted cell, for a held row.
    bool rolled;
    // Only cells within `within` metres of here when that is above nought.
    float x;
    float y;
    float within;
};

// The unit vector the cells drift toward at `seconds`.
void Heading(double seconds, float out[2]);

// The cells alive, as many as `capacity` holds, nearest the query's point first when there are more.
int CellsAt(const CellQuery& query, Cell* out, int capacity);

// How much of a point a cell holds: its core and its heart, the storm-warning sky ahead along its
// track, and the clearing behind it.
struct Touch {
    float core;
    float heart;
    float ahead;
    float wake;
};
Touch TouchOf(const Cell& cell, const float heading[2], float x, float y);

}
