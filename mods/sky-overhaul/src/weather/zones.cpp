#include "weather/zones.h"

#include <algorithm>
#include <cmath>
#include <cstdint>

namespace {
#include "weather/zones.inc"

    // How far apart the two worlds' jungle has to be under the player for a frame to count, how many
    // more frames one world needs than the other to be taken, and the most a score keeps.
    constexpr float kTelling = 0.4f;
    constexpr int kSure = 60;
    constexpr int kScoreCap = 180;

    SkyOverhaul::Weather::Ground Cell(int world, float x, float y) {
        const int column = static_cast<int>(std::floor(x / kZoneCellSize));
        const int row = static_cast<int>(std::floor(y / kZoneCellSize));
        if (column < 0 || row < 0 || column >= kZoneCells || row >= kZoneCells) {
            return {0.0f, 0.0f, 0.0f};
        }
        const uint8_t* cell = kZones[world - 1][row][column];
        return {cell[0] / 255.0f, cell[1] / 255.0f, cell[2] / 255.0f};
    }
}

SkyOverhaul::Weather::Ground SkyOverhaul::Weather::GroundAt(int world, float x, float y) {
    if (world == 1 || world == 2) {
        return Cell(world, x, y);
    }
    const Ground one = Cell(1, x, y);
    const Ground two = Cell(2, x, y);
    return {0.5f * (one.savannah + two.savannah), 0.5f * (one.jungle + two.jungle),
            0.5f * (one.desert + two.desert)};
}

void SkyOverhaul::Weather::WorldGuess::Observe(float x, float y, float jungle) {
    const float one = std::fabs(Cell(1, x, y).jungle - jungle);
    const float two = std::fabs(Cell(2, x, y).jungle - jungle);
    if (std::fabs(one - two) < kTelling) {
        return;
    }
    m_score = std::clamp(m_score + (one < two ? 1 : -1), -kScoreCap, kScoreCap);
    if (m_score >= kSure) {
        m_world = 1;
    } else if (m_score <= -kSure) {
        m_world = 2;
    }
}
