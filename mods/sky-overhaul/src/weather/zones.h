// Where each campaign world's jungle, savannah and desert lie, baked from its sound region layer by
// tools/bake_zones.py, and which world the player is in.
#pragma once

namespace SkyOverhaul::Weather {

// The zones' shares of the land at a point; all nought off the map. The sound layer marks the ring
// round the playable land as desert.
struct Ground {
    float savannah;
    float jungle;
    float desert;
};

// `world` is 1 or 2; 0, not known, gives the two worlds' shares evenly mixed.
Ground GroundAt(int world, float x, float y);

// Works out the world from the game's own jungle share under the player against both grids, the
// guess moving only on clear and repeated evidence so it never flickers.
class WorldGuess {
public:
    void Observe(float x, float y, float jungle);
    int World() const { return m_world; }

private:
    int m_score = 0;
    int m_world = 0;
};

}
