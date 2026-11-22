#include "weather/weather.h"

#include "engine/seqlock.h"
#include "engine/time_of_day.h"
#include "sky_model.h"
#include "weather/zones.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <mutex>

namespace {
    // Inside the environment manager: the zones' current shares, and the sun as its sky last set it.
    constexpr size_t kSavannah = 0x230;
    constexpr size_t kJungle = 0x244;
    constexpr size_t kDesert = 0x258;
    constexpr size_t kSunHeight = 0x270 + 0x44 + 8;

    // How dark the game's full storm makes the cloud base, with the weather off.
    constexpr float kStormBaseShade = 0.8f;

    // A fog ending this far off leaves the land at the horizon showing, and this near swallows it.
    constexpr float kVeilFar = 400.0f;
    constexpr float kVeilNear = 200.0f;

    constexpr double kPi = 3.14159265358979;

    struct Published {
        bool on;
        SkyOverhaul::Weather::Reading reading;
    };

    // A planting asked for, waiting for the game's thread to know the clock and the camera.
    struct Asked {
        bool pending;
        float degrees;
        float distance;
    };

    std::atomic<bool> g_enabled{false};
    std::atomic<int> g_forced{-1};
    std::mutex g_askedLock;
    Asked g_asked = {};

    SkyOverhaul::Seqlock<Published> g_latest;
    SkyOverhaul::Seqlock<double> g_clock;
    // The camera's position, and the way it looks in degrees.
    SkyOverhaul::Seqlock<std::array<float, 4>> g_eye;
    SkyOverhaul::Seqlock<SkyOverhaul::Weather::MapCellList> g_cells;

    // The game thread's own copies, for the hooks that run on it after Update.
    Published g_engine = {};
    SkyOverhaul::Weather::MapCellList g_map = {};
    SkyOverhaul::Weather::WorldGuess g_world;
    SkyOverhaul::Weather::Planted g_planted = {0.0f, 0.0f, -1.0};

    float Float(const uint8_t* manager, size_t offset) {
        return *reinterpret_cast<const float*>(manager + offset);
    }

    // Takes a planting asked for since the last frame.
    void TakePlanting(double seconds, const std::array<float, 4>& eye) {
        Asked asked;
        {
            std::lock_guard<std::mutex> lock(g_askedLock);
            asked = g_asked;
            g_asked.pending = false;
        }
        if (!asked.pending) {
            return;
        }
        if (asked.distance < 0.0f) {
            g_planted.at = -1.0;
            return;
        }
        const double angle = asked.degrees * kPi / 180.0;
        g_planted = {eye[0] + asked.distance * static_cast<float>(std::cos(angle)),
                     eye[1] + asked.distance * static_cast<float>(std::sin(angle)), seconds};
    }
}

void SkyOverhaul::Weather::SetEnabled(bool enabled) {
    g_enabled.store(enabled, std::memory_order_relaxed);
}

void SkyOverhaul::Weather::Update(const uint8_t* manager) {
    double seconds = 0.0;
    if (!TimeOfDay::Elapsed(seconds)) {
        return;
    }
    g_clock.Publish(seconds);

    g_engine.on = g_enabled.load(std::memory_order_relaxed);
    g_map.count = 0;
    if (g_engine.on) {
        std::array<float, 4> eye = {};
        if (g_eye.Latest(eye)) {
            g_world.Observe(eye[0], eye[1], Float(manager, kJungle));
        }
        TakePlanting(seconds, eye);
        const Moment moment = {seconds, Float(manager, kSunHeight), Float(manager, kSavannah),
                               Float(manager, kJungle), Float(manager, kDesert), eye[0], eye[1],
                               g_world.World(), g_planted};
        const int forced = g_forced.load(std::memory_order_relaxed);
        g_engine.reading = forced >= 0 ? Hold(forced, moment) : Read(moment);
        Heading(seconds, g_map.heading);
        g_map.count = MapCells(moment, forced < 0, g_map.cells, kMapCells);
    }
    g_latest.Publish(g_engine);
    g_cells.Publish(g_map);
}

bool SkyOverhaul::Weather::ForEngine(Reading& out) {
    if (!g_engine.on) {
        return false;
    }
    out = g_engine.reading;
    return true;
}

bool SkyOverhaul::Weather::Latest(Reading& out) {
    Published published;
    if (!g_latest.Latest(published) || !published.on) {
        return false;
    }
    out = published.reading;
    return true;
}

bool SkyOverhaul::Weather::LatestCells(MapCellList& out) {
    return g_cells.Latest(out);
}

bool SkyOverhaul::Weather::Clock(double& seconds) {
    return g_clock.Latest(seconds);
}

void SkyOverhaul::Weather::Force(int row) {
    g_forced.store(row >= 0 && static_cast<size_t>(row) < RowCount() ? row : -1,
                   std::memory_order_relaxed);
}

int SkyOverhaul::Weather::Forced() {
    return g_forced.load(std::memory_order_relaxed);
}

void SkyOverhaul::Weather::Plant(float degrees, float distance) {
    std::lock_guard<std::mutex> lock(g_askedLock);
    g_asked = {true, degrees, distance};
}

void SkyOverhaul::Weather::SetEye(const float eye[3], const float direction[3]) {
    const float degrees =
        static_cast<float>(std::atan2(direction[1], direction[0]) * 180.0 / kPi);
    g_eye.Publish({eye[0], eye[1], eye[2], degrees});
}

bool SkyOverhaul::Weather::Eye(float out[3]) {
    std::array<float, 4> eye;
    if (!g_eye.Latest(eye)) {
        return false;
    }
    std::copy_n(eye.begin(), 3, out);
    return true;
}

bool SkyOverhaul::Weather::Facing(float& degrees) {
    std::array<float, 4> eye;
    if (!g_eye.Latest(eye)) {
        return false;
    }
    degrees = eye[3];
    return true;
}

int SkyOverhaul::Weather::Showing() {
    Reading reading;
    if (!Latest(reading)) {
        return -1;
    }
    return reading.blend < 0.5f ? reading.row : reading.next;
}

SkyOverhaul::Weather::Air SkyOverhaul::Weather::AirNow(float engineStorm) {
    Reading reading;
    if (Latest(reading)) {
        const Levers& levers = reading.levers;
        const float end = FogOf(levers).end;
        const float close =
            end > 0.0f ? std::clamp((kVeilFar - end) / (kVeilFar - kVeilNear), 0.0f, 1.0f) : 0.0f;
        return {true,
                reading.sky,
                levers.storminess,
                SkyModel::Haze(levers.storminess) * levers.haze,
                reading.sky.shade,
                levers.heat,
                levers.milk,
                levers.fogWeight * close * close * (3.0f - 2.0f * close),
                Cover(levers)};
    }
    const float storminess = SkyModel::Storminess(engineStorm);
    return {false, {}, storminess, SkyModel::Haze(storminess), storminess * kStormBaseShade,
            1.0f - storminess, 0.0f, 0.0f, 0.0f};
}
