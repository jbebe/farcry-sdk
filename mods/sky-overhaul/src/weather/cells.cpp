#include "weather/cells.h"

#include "blend.h"
#include "hash.h"
#include "weather/timeline.h"
#include "weather/zones.h"

#include <algorithm>
#include <cmath>
#include <iterator>

namespace {
    using SkyOverhaul::Weather::Cell;

    constexpr double kHour = 3600.0;
    constexpr uint32_t kSeed = 0xCE115u;

    // Cells start in slots this long, each slot rolling as many tries as its hour and air allow.
    constexpr double kSlot = 900.0;

    // How long a cell lives, the share of its life it only gathers for, over how much more it turns
    // to rain, and the share from which it dies away.
    constexpr double kShortestLife = 1.5 * kHour;
    constexpr double kLongestLife = 3.0 * kHour;
    constexpr float kGathering = 0.25f;
    constexpr float kRaining = 0.2f;
    constexpr float kDying = 0.7f;
    // From what share of its life the ground behind it stops showing the clearing, and for how long
    // after rain the ground counts as clearing.
    constexpr float kClearingFades = 0.85f;
    constexpr double kClearing = 1.5 * kHour;

    // How far out its cloud reaches.
    constexpr float kSmallest = 1500.0f;
    constexpr float kLargest = 3500.0f;

    // Metres a clock second the cells drift, the speed the weather under the clouds drifts at.
    constexpr double kSpeed = 0.45;

    // Where cells start: a square this wide round the middle of the campaign world.
    constexpr float kMiddle = 2560.0f;
    constexpr float kSpread = 40960.0f;

    // Tries a slot rolls at the afternoon's peak in unstable air, each kept by its ground's fuel
    // against the most any ground gives.
    constexpr float kPeakTries = 36.0f;
    constexpr float kJungleFuel = 1.8f;
    constexpr float kSavannahFuel = 0.9f;
    constexpr float kDesertFuel = 0.15f;
    constexpr float kBeyondFuel = 1.0f;
    constexpr float kMostFuel = kJungleFuel;
    // The odds a cell grows a heavy heart, against one of fuel.
    constexpr float kStrongOdds = 0.3f;

    // The hour cells start most at, over how many hours either side that falls away, and the share
    // left at night.
    constexpr float kPeakHour = 15.0f;
    constexpr float kPeakWidth = 3.0f;
    constexpr float kNightCells = 0.2f;

    // As many cells as can be alive at once over the whole square.
    constexpr int kMostAlive = 512;

    float Roll(long long slot, int i, int k, uint32_t seed) {
        return SkyOverhaul::Random(static_cast<int>(slot), i, k, seed);
    }

    using SkyOverhaul::Smooth;

    float Diurnal(double seconds) {
        const double hour = std::fmod(seconds, 24.0 * kHour) / kHour;
        const float off = static_cast<float>(std::fmod(hour - kPeakHour + 36.0, 24.0) - 12.0);
        return kNightCells + (1.0f - kNightCells) * std::exp(-(off * off) / (kPeakWidth * kPeakWidth));
    }

    float Fuel(int world, float x, float y) {
        const SkyOverhaul::Weather::Ground ground = SkyOverhaul::Weather::GroundAt(world, x, y);
        const float beyond = 1.0f - ground.savannah - ground.jungle - ground.desert;
        return kSavannahFuel * ground.savannah + kJungleFuel * ground.jungle +
               kDesertFuel * ground.desert + kBeyondFuel * (std::max)(beyond, 0.0f);
    }

    // A cell `age` seconds into a life of `life`, started at `x`, `y` when it was `drifted` back.
    Cell Make(float x, float y, const double drifted[2], float radius, double age, double life,
              bool strong) {
        const float share = static_cast<float>(age / life);
        const double rained = std::clamp(age - kGathering * life, 0.0, kClearing);
        Cell cell;
        cell.x = x - static_cast<float>(drifted[0]);
        cell.y = y - static_cast<float>(drifted[1]);
        cell.radius = radius;
        cell.strength = Smooth(share / kGathering) * (1.0f - Smooth((share - kDying) / (1.0f - kDying)));
        cell.mature = Smooth((share - kGathering) / kRaining);
        cell.reach = static_cast<float>(kSpeed * (kDying * life - age)) - 0.5f * radius;
        cell.wake = static_cast<float>(kSpeed * rained);
        cell.clearing =
            cell.mature * (1.0f - Smooth((share - kClearingFades) / (1.0f - kClearingFades)));
        cell.strong = strong;
        return cell;
    }

    // The planted cell: always strong, at its full when put down, and as long-lived as any.
    constexpr float kPlantedRadius = 2500.0f;
    constexpr float kPlantedAge = kGathering + kRaining;

    struct Found {
        Cell cells[kMostAlive];
        float distances[kMostAlive];
        int count = 0;

        void Add(const Cell& cell, const SkyOverhaul::Weather::CellQuery& query) {
            const float distance = std::hypot(cell.x - query.x, cell.y - query.y);
            if (query.within > 0.0f && distance > query.within + cell.radius) {
                return;
            }
            if (count < kMostAlive) {
                cells[count] = cell;
                distances[count] = distance;
                count++;
            }
        }
    };
}

void SkyOverhaul::Weather::Heading(double seconds, float out[2]) {
    const double angle = DriftAngle(seconds);
    out[0] = static_cast<float>(-std::cos(angle));
    out[1] = static_cast<float>(-std::sin(angle));
}

int SkyOverhaul::Weather::CellsAt(const CellQuery& query, Cell* out, int capacity) {
    const double t = query.seconds;
    double now[2];
    Drift(t, kSpeed, now);
    static thread_local Found found;
    found.count = 0;

    const uint32_t seed = kSeed + static_cast<uint32_t>(query.world) * 0x9E3779B9u;
    const long long last = static_cast<long long>(std::floor(t / kSlot));
    const long long first = static_cast<long long>(std::floor((t - kLongestLife) / kSlot));
    for (long long slot = query.rolled ? first : last + 1; slot <= last; slot++) {
        const double start = static_cast<double>(slot) * kSlot;
        const float expected = kPeakTries * Diurnal(start) * Instability(start);
        const int tries = static_cast<int>(expected + Roll(slot, 0, 0, seed));
        for (int i = 1; i <= tries; i++) {
            const double born = start + Roll(slot, i, 0, seed) * kSlot;
            const double life = kShortestLife + (kLongestLife - kShortestLife) * Roll(slot, i, 1, seed);
            const double age = t - born;
            if (age < 0.0 || age >= life) {
                continue;
            }
            const float x = kMiddle + (Roll(slot, i, 2, seed) - 0.5f) * kSpread;
            const float y = kMiddle + (Roll(slot, i, 3, seed) - 0.5f) * kSpread;
            const float fuel = Fuel(query.world, x, y);
            if (Roll(slot, i, 4, seed) * kMostFuel >= fuel) {
                continue;
            }
            double then[2];
            Drift(born, kSpeed, then);
            const double drifted[2] = {now[0] - then[0], now[1] - then[1]};
            const float radius = kSmallest + (kLargest - kSmallest) * Roll(slot, i, 5, seed);
            const bool strong = Roll(slot, i, 6, seed) < kStrongOdds * fuel;
            found.Add(Make(x, y, drifted, radius, age, life, strong), query);
        }
    }

    const Planted& planted = query.planted;
    if (planted.at >= 0.0) {
        const double life = kLongestLife;
        const double age = t - planted.at + kPlantedAge * life;
        if (age >= 0.0 && age < life) {
            double then[2];
            Drift(planted.at, kSpeed, then);
            const double drifted[2] = {now[0] - then[0], now[1] - then[1]};
            found.Add(Make(planted.x, planted.y, drifted, kPlantedRadius, age, life, true), query);
        }
    }

    const int count = (std::min)(found.count, capacity);
    if (found.count > capacity) {
        int order[kMostAlive];
        for (int i = 0; i < found.count; i++) {
            order[i] = i;
        }
        std::partial_sort(order, order + count, order + found.count,
                          [](int a, int b) { return found.distances[a] < found.distances[b]; });
        for (int i = 0; i < count; i++) {
            out[i] = found.cells[order[i]];
        }
    } else {
        std::copy_n(found.cells, count, out);
    }
    return count;
}

SkyOverhaul::Weather::Touch SkyOverhaul::Weather::TouchOf(const Cell& cell, const float heading[2],
                                                          float x, float y) {
    const float r = cell.radius;
    const float dx = (x - cell.x) / r;
    const float dy = (y - cell.y) / r;
    const float distance = std::sqrt(dx * dx + dy * dy);
    const float along = dx * heading[0] + dy * heading[1];
    const float across = std::fabs(dy * heading[0] - dx * heading[1]);

    // The core holds a point within half its reach and lets go by its edge; the warning ahead and
    // the clearing behind run only as wide as the core sweeps, so a warning is always followed by
    // the core passing over.
    Touch touch;
    touch.core = (1.0f - Smooth((distance - 0.45f) / 0.55f)) * cell.strength;
    touch.heart = cell.strong ? (1.0f - Smooth((distance - 0.1f) / 0.35f)) * cell.strength * cell.mature
                              : 0.0f;
    const float swath = 1.0f - Smooth((across - 0.35f) / 0.15f);
    const float reach = cell.reach / r;
    touch.ahead = Smooth((along - 0.3f) / 0.3f) * (1.0f - Smooth((along - 0.6f - reach) / 0.5f)) *
                  swath * cell.mature;
    const float wake = cell.wake / r;
    touch.wake = Smooth((-along - 0.5f) / 0.3f) * (1.0f - Smooth((-along - 0.5f - wake) / 0.5f)) *
                 swath * cell.clearing;
    return touch;
}
