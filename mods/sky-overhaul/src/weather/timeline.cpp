#include "weather/timeline.h"

#include "hash.h"

#include <algorithm>
#include <cmath>
#include <iterator>

namespace {
    using SkyOverhaul::Weather::Family;
    using SkyOverhaul::Weather::Levers;

    constexpr double kHour = 3600.0;
    constexpr double kDay = 24.0 * kHour;
    constexpr double kPi = 3.14159265358979;
    constexpr uint32_t kSeed = 0x57EA7u;

    // How long an air mass lasts on average, how far either way its start moves, and over how long
    // one turns into the next; a storm's own stages turn faster.
    constexpr double kSegment = 4.0 * kHour;
    constexpr double kJitter = 0.5 * kHour;
    constexpr double kSegmentBlend = 0.75 * kHour;
    constexpr double kStageBlend = kHour / 3.0;

    // Each family's share of the air mass's time per zone, in percent, and the share of the world
    // each zone covers, which the world-wide air mass follows. Its storms are the fronts; the cells
    // bring the rest of a zone's storms.
    struct Share {
        Family family;
        float jungle;
        float savannah;
        float desert;
    };
    constexpr Share kShares[] = {
        {Family::Clear, 35.0f, 55.0f, 65.0f},
        {Family::Foggy, 10.0f, 5.0f, 0.0f},
        {Family::Overcast, 25.0f, 10.0f, 2.0f},
        {Family::Storm, 8.0f, 5.0f, 1.0f},
        {Family::Wind, 5.0f, 15.0f, 30.0f},
    };

    // How readily each family's air lets cells grow, a dusty row's dry air less; fronts and grey
    // skies bring their own rain.
    constexpr float kFamilyInstability[] = {1.0f, 0.2f, 0.5f, 0.5f, 0.3f};
    constexpr float kDryAir = 0.8f;

    // The cells: how far off one can still touch the player, how many near ones count, and how much
    // of the jungle's hold they keep over savannah and desert.
    constexpr float kNearCells = 7500.0f;
    constexpr int kNearCount = 32;
    constexpr float kSavannahHold = 0.6f;
    constexpr float kDesertHold = 0.2f;

    // Since-rain is looked up on a grid of player positions this fine, so a frame reuses the last
    // frame's look back.
    constexpr float kSinceGrid = 32.0f;
    constexpr float kWorldJungle = 0.4f;
    constexpr float kWorldSavannah = 0.5f;
    constexpr float kWorldDesert = 0.1f;

    struct Pick {
        const char* id;
        float weight;
    };
    constexpr Pick kClear[] = {{"C1", 10.0f}, {"C2", 15.0f}, {"C3", 10.0f}, {"C4", 20.0f},
                               {"C5", 25.0f}, {"C6", 10.0f}, {"C7", 10.0f}};
    constexpr Pick kFoggy[] = {{"F1", 1.0f}, {"F2", 1.0f}};
    constexpr Pick kOvercast[] = {{"O1", 5.0f}, {"O2", 3.0f}, {"O3", 2.0f}};
    constexpr Pick kWind[] = {{"W1", 6.0f}, {"W2", 4.0f}};

    // A storm's course through its air mass: each stage and the share of the air mass it starts at.
    struct Stage {
        const char* id;
        float from;
    };
    constexpr Stage kStorm[] = {{"F3", 0.0f}, {"S1", 0.33f}, {"S2", 0.43f}, {"S4", 0.70f}};
    constexpr Stage kStrongStorm[] = {{"F3", 0.0f}, {"S1", 0.33f}, {"S2", 0.43f},
                                      {"S3", 0.53f}, {"S2", 0.65f}, {"S4", 0.73f}};
    constexpr float kStrongShare = 0.35f;

    // Morning fog and mist after rain: how far off each ends, the white they share, the morning
    // fog's odds per zone, the wind from which mist starts to thin and the one it is gone by, the
    // most a night may blow and still leave fog by morning, and how long rain stays.
    constexpr float kMorningFog = 80.0f;
    constexpr float kRainMist = 200.0f;
    constexpr float kMistTint[3] = {1.0f, 1.03f, 1.06f};
    // How far above the player's ground mist has thinned out: a shallow layer that hilltops stand out of.
    constexpr float kMistTop = 35.0f;
    constexpr float kJungleMorningFog = 0.7f;
    constexpr float kSavannahMorningFog = 0.3f;
    constexpr float kStillAir = 30.0f;
    constexpr float kMistGone = 100.0f;
    constexpr float kFoggyNight = 50.0f;
    constexpr float kWetRain = 0.15f;
    constexpr double kDrying = 2.0 * kHour;
    constexpr double kMistRising = 600.0;
    constexpr double kMistAfterRain = 1.0 * kHour;
    constexpr double kLookBack = 600.0;
    constexpr int kLookBackSteps = 12;
    constexpr int kStopHalvings = 6;
    // Game seconds of a downpour that fill the puddles, how long rain stays in them, and the least
    // rain that puts any in.
    constexpr double kPuddleFill = 1800.0;
    constexpr float kPuddleRain = 0.05f;
    constexpr double kPuddleDrain = 3.0 * kHour;
    constexpr int kPuddleSteps = static_cast<int>(kPuddleDrain / kLookBack);
    // Game seconds a rainbow takes to come up in the shower moving off once the rain here stops, and
    // to fade after that.
    constexpr double kBowRise = 720.0;
    constexpr double kBowFade = 1200.0;

    // Rain's own fog: where it ends in drizzle, steady rain and a downpour, grey, and bluer as the
    // rain comes down harder than steady. It is spray through the whole air, lighter than the
    // storm's own dark. None of it hangs at the eye, which would fog the player's own hands.
    constexpr float kDrizzle = 0.3f;
    constexpr float kSteadyRain = 0.65f;
    constexpr float kDrizzleFog = 200.0f;
    constexpr float kSteadyRainFog = 110.0f;
    constexpr float kDownpourFog = 60.0f;
    constexpr float kRainTint[3] = {1.1f, 1.12f, 1.14f};
    constexpr float kDownpourTint[3] = {1.0f, 1.03f, 1.08f};

    // Dust: where its fog ends light and thick, its colour, the share of days with none of their
    // own, how much of a dusty day's dust hangs in still air, and the savannah's share of the
    // desert's dust. The jungle has none. In still air it is haze through the whole air that turns
    // the sky milky; the wind from a breeze up lifts it off the ground as a layer lying there.
    constexpr float kLightDust = 200.0f;
    constexpr float kThickDust = 80.0f;
    constexpr float kDustTint[3] = {1.15f, 0.98f, 0.75f};
    constexpr float kCrispDays = 0.4f;
    constexpr float kStillDust = 0.35f;
    constexpr float kSavannahDust = 0.6f;
    constexpr float kCalmWind = 20.0f;
    constexpr float kBreezeWind = 45.0f;
    constexpr float kDustyWind = 110.0f;
    constexpr float kDustMilk = 0.8f;
    // How far above the player's ground lifted dust has thinned out: deep enough that the far hills
    // Flatten draws low stay inside it.
    constexpr float kDustTop = 150.0f;

    float Roll(long long a, int b) {
        return SkyOverhaul::Random(static_cast<int>(a), b, 0, kSeed);
    }

    float Clamp01(float value) {
        return std::clamp(value, 0.0f, 1.0f);
    }

    float Mix(float a, float b, float t) {
        return a + (b - a) * t;
    }

    float Smooth(float t) {
        t = Clamp01(t);
        return t * t * (3.0f - 2.0f * t);
    }

    Levers RowLevers(int row) {
        return SkyOverhaul::Weather::FromRow(SkyOverhaul::Weather::Rows()[row]);
    }

    template <size_t N>
    int PickRow(const Pick (&picks)[N], float roll) {
        float total = 0.0f;
        for (const Pick& pick : picks) {
            total += pick.weight;
        }
        float reach = roll * total;
        for (const Pick& pick : picks) {
            reach -= pick.weight;
            if (reach < 0.0f) {
                return SkyOverhaul::Weather::Find(pick.id);
            }
        }
        return SkyOverhaul::Weather::Find(picks[N - 1].id);
    }

    Family PickFamily(float roll) {
        float total = 0.0f;
        for (const Share& share : kShares) {
            total += share.jungle * kWorldJungle + share.savannah * kWorldSavannah +
                     share.desert * kWorldDesert;
        }
        float reach = roll * total;
        for (const Share& share : kShares) {
            reach -= share.jungle * kWorldJungle + share.savannah * kWorldSavannah +
                     share.desert * kWorldDesert;
            if (reach < 0.0f) {
                return share.family;
            }
        }
        return Family::Clear;
    }

    // A row from `start`, turned into from the row before it over `blend`, centred on `start`.
    struct Key {
        double start;
        double blend;
        int row;
    };

    double Start(long long segment) {
        return static_cast<double>(segment) * kSegment +
               (Roll(segment, 0) * 2.0 - 1.0) * kJitter;
    }

    template <size_t N>
    void AddStages(const Stage (&stages)[N], double start, double end, Key* keys, int& count) {
        for (size_t i = 0; i < N; i++) {
            keys[count++] = {start + (end - start) * stages[i].from,
                             i == 0 ? kSegmentBlend : kStageBlend,
                             SkyOverhaul::Weather::Find(stages[i].id)};
        }
    }

    void AddKeys(long long segment, Key* keys, int& count) {
        const double start = Start(segment);
        const double end = Start(segment + 1);
        const Family family = PickFamily(Roll(segment, 1));
        if (family == Family::Storm) {
            if (Roll(segment, 3) < kStrongShare) {
                AddStages(kStrongStorm, start, end, keys, count);
            } else {
                AddStages(kStorm, start, end, keys, count);
            }
            return;
        }
        const float roll = Roll(segment, 2);
        const int row = family == Family::Foggy      ? PickRow(kFoggy, roll)
                        : family == Family::Overcast ? PickRow(kOvercast, roll)
                        : family == Family::Wind     ? PickRow(kWind, roll)
                                                     : PickRow(kClear, roll);
        keys[count++] = {start, kSegmentBlend, row};
    }

    struct AirMass {
        Levers levers;
        int row;
        int next;
        float blend;
        double left;
    };

    AirMass AirMassAt(double t) {
        const long long segment = static_cast<long long>(std::floor(t / kSegment));
        Key keys[4 * std::size(kStrongStorm)];
        int count = 0;
        for (long long k = segment - 1; k <= segment + 2; k++) {
            AddKeys(k, keys, count);
        }
        // No blend reaches past halfway to a neighbour, so two never overlap.
        for (int i = 1; i + 1 < count; i++) {
            keys[i].blend = (std::min)({keys[i].blend, keys[i].start - keys[i - 1].start,
                                        keys[i + 1].start - keys[i].start});
        }
        int at = 0;
        for (int i = 0; i < count; i++) {
            if (keys[i].start <= t) {
                at = i;
            }
        }

        const Key& key = keys[at];
        AirMass air = {RowLevers(key.row), key.row, key.row, 0.0f,
                       at + 1 < count ? keys[at + 1].start - t : 0.0};
        if (at > 0 && t < key.start + key.blend * 0.5) {
            const float w = Smooth(static_cast<float>((t - key.start) / key.blend + 0.5));
            const int previous = keys[at - 1].row;
            air.levers = SkyOverhaul::Weather::Lerp(RowLevers(previous), air.levers, w);
            air.row = previous;
            air.next = key.row;
            air.blend = w;
        } else if (at + 1 < count && t > keys[at + 1].start - keys[at + 1].blend * 0.5) {
            const Key& next = keys[at + 1];
            const float w = Smooth(static_cast<float>((t - next.start) / next.blend + 0.5));
            air.levers = SkyOverhaul::Weather::Lerp(air.levers, RowLevers(next.row), w);
            air.next = next.row;
            air.blend = w;
        }
        return air;
    }

    // The air mass with the cells near the player laid over it.
    struct Local {
        Levers levers;
        // A cell's row while one holds the player, else -1; how much of the weather is the cells';
        // metres to the nearest cell's centre, below nought for none near.
        int row;
        float share;
        float distance;
    };

    Local WithCells(const Levers& air, const SkyOverhaul::Weather::Moment& moment, double t,
                    bool rolled) {
        using namespace SkyOverhaul::Weather;
        Local local = {air, -1, 0.0f, -1.0f};
        Cell cells[kNearCount];
        const int count = CellsAt({t, moment.world, moment.planted, rolled, moment.x, moment.y,
                                   kNearCells},
                                  cells, kNearCount);
        if (count == 0) {
            return local;
        }
        float heading[2];
        Heading(t, heading);
        float core = 0.0f;
        float heart = 0.0f;
        float ahead = 0.0f;
        float wake = 0.0f;
        float cores = 0.0f;
        float rained = 0.0f;
        local.distance = std::hypot(cells[0].x - moment.x, cells[0].y - moment.y);
        for (int i = 0; i < count; i++) {
            const Touch touch = TouchOf(cells[i], heading, moment.x, moment.y);
            core = (std::max)(core, touch.core);
            cores += touch.core;
            rained += touch.core * cells[i].mature;
            heart = (std::max)(heart, touch.heart);
            ahead = (std::max)(ahead, touch.ahead);
            wake = (std::max)(wake, touch.wake);
            local.distance =
                (std::min)(local.distance, std::hypot(cells[i].x - moment.x, cells[i].y - moment.y));
        }
        const float mature = cores > 0.0f ? rained / cores : 0.0f;
        const float zones = moment.savannah + moment.jungle + moment.desert;
        const float hold =
            zones > 0.0f ? (moment.jungle + kSavannahHold * moment.savannah + kDesertHold * moment.desert) /
                               zones
                         : 1.0f;
        core *= hold;
        heart *= hold;
        // A front's own warning and storm outweigh a cell's warning and clearing.
        const float calm = 1.0f - Smooth((air.storminess - 0.3f) / 0.4f);
        ahead *= hold * calm;
        wake *= hold * calm;

        static const int s1 = Find("S1");
        static const int s2 = Find("S2");
        static const int s3 = Find("S3");
        static const int s4 = Find("S4");
        static const int f3 = Find("F3");
        const float heavy = core > 0.0f ? (std::min)(heart / core, 1.0f) : 0.0f;
        const Levers storm = Lerp(Lerp(RowLevers(s1), RowLevers(s2), mature), RowLevers(s3), heavy);
        local.levers = Lerp(local.levers, RowLevers(s4), wake);
        local.levers = Lerp(local.levers, RowLevers(f3), ahead);
        local.levers = Lerp(local.levers, storm, core);

        // The row showing is the one with the largest share of what is seen, each laid over the
        // ones before it.
        const float shares[4] = {(1.0f - wake) * (1.0f - ahead) * (1.0f - core),
                                 wake * (1.0f - ahead) * (1.0f - core), ahead * (1.0f - core), core};
        const int most = static_cast<int>(std::max_element(shares, shares + 4) - shares);
        const int rows[4] = {-1, s4, f3, heavy >= 0.5f ? s3 : mature >= 0.5f ? s2 : s1};
        local.row = rows[most];
        local.share = 1.0f - shares[0];
        return local;
    }

    bool Raining(double t, const SkyOverhaul::Weather::Moment& moment) {
        return WithCells(AirMassAt(t).levers, moment, t, true).levers.rain > kWetRain;
    }

    // Where between a moment it rained and a later one it did not the rain stopped.
    double Stopped(double wet, double dry, int halvings, const SkyOverhaul::Weather::Moment& moment) {
        for (int halving = 0; halving < halvings; halving++) {
            const double middle = 0.5 * (dry + wet);
            (Raining(middle, moment) ? wet : dry) = middle;
        }
        return 0.5 * (dry + wet);
    }

    // Game seconds before `t`, a point of the look-back lattice, since rain last fell here: nought
    // if it falls at `t`, below nought for none lately. The look back steps back over the lattice
    // until it finds rain, so every moment between two lattice points finds the same rain.
    double LookBack(double t, const SkyOverhaul::Weather::Moment& moment) {
        for (int step = 0; step <= kLookBackSteps; step++) {
            const double wet = t - step * kLookBack;
            if (Raining(wet, moment)) {
                return step == 0 ? 0.0 : t - Stopped(wet, wet + kLookBack, kStopHalvings, moment);
            }
        }
        return -1.0;
    }

    // Game seconds since rain last fell here, nought while it falls, below nought for none lately:
    // the look back from the lattice point before `t`, kept while the player stays on one spot of
    // the since-rain grid.
    double SinceRain(double t, const Levers& now, const SkyOverhaul::Weather::Moment& moment) {
        if (now.rain > kWetRain) {
            return 0.0;
        }
        struct Kept {
            double at;
            float x;
            float y;
            int world;
            double planted;
            double since;
        };
        static thread_local Kept kept = {-1.0};
        SkyOverhaul::Weather::Moment at = moment;
        at.seconds = std::floor(t / kLookBack) * kLookBack;
        at.x = std::round(moment.x / kSinceGrid) * kSinceGrid;
        at.y = std::round(moment.y / kSinceGrid) * kSinceGrid;
        if (kept.at != at.seconds || kept.x != at.x || kept.y != at.y || kept.world != at.world ||
            kept.planted != at.planted.at) {
            kept = {at.seconds, at.x, at.y, at.world, at.planted.at, LookBack(at.seconds, at)};
        }
        if (kept.since != 0.0) {
            return kept.since < 0.0 ? kept.since : kept.since + (t - at.seconds);
        }
        return t - Stopped(at.seconds, t, kStopHalvings + 2, at);
    }

    // How full the puddles are at `t`: the rain of each look-back step, as much as has fallen of it
    // by now, each counted less the longer ago it fell, until gone after kPuddleDrain. The steps'
    // rain is kept while the player stays on one spot of the since-rain grid.
    float Puddles(double t, const SkyOverhaul::Weather::Moment& moment) {
        struct Kept {
            double at;
            float x;
            float y;
            int world;
            double planted;
            float rain[kPuddleSteps + 1];
        };
        static thread_local Kept kept = {-1.0};
        SkyOverhaul::Weather::Moment at = moment;
        at.seconds = std::floor(t / kLookBack) * kLookBack;
        at.x = std::round(moment.x / kSinceGrid) * kSinceGrid;
        at.y = std::round(moment.y / kSinceGrid) * kSinceGrid;
        if (kept.at != at.seconds || kept.x != at.x || kept.y != at.y || kept.world != at.world ||
            kept.planted != at.planted.at) {
            kept = {at.seconds, at.x, at.y, at.world, at.planted.at};
            for (int step = 0; step <= kPuddleSteps; step++) {
                const double start = at.seconds - step * kLookBack;
                const float rain = WithCells(AirMassAt(start).levers, at, start, true).levers.rain;
                kept.rain[step] = rain >= kPuddleRain ? rain : 0.0f;
            }
        }
        double fill = 0.0;
        for (int step = 0; step <= kPuddleSteps; step++) {
            const double start = at.seconds - step * kLookBack;
            const double fallen = step == 0 ? t - at.seconds : kLookBack;
            const double age = t - (start + 0.5 * fallen);
            fill += kept.rain[step] * fallen / kPuddleFill * (std::max)(0.0, 1.0 - age / kPuddleDrain);
        }
        return Clamp01(static_cast<float>(fill));
    }

    // How much morning fog the hour and the night before allow, before the zone's odds.
    float MorningFog(double t, float sunHeight) {
        const double day = std::floor(t / kDay);
        const float hour = static_cast<float>((t - day * kDay) / kHour);
        float envelope = 0.0f;
        if (hour >= 1.0f && hour < 5.0f) {
            envelope = Smooth((hour - 1.0f) / 4.0f);
        } else if (hour >= 5.0f && hour < 12.0f) {
            envelope = 1.0f - Smooth((sunHeight - 0.05f) / 0.21f);
        }
        if (envelope <= 0.0f) {
            return 0.0f;
        }
        const Levers night = AirMassAt(day * kDay + 3.0 * kHour).levers;
        const float clear = 1.0f - Smooth((night.coverage - 0.5f) / 0.2f);
        const float dry = night.rain > 0.01f ? 0.0f : 1.0f;
        const float still = night.wind <= kFoggyNight ? 1.0f : 0.0f;
        return envelope * clear * dry * still;
    }

    float Still(float wind) {
        return 1.0f - Smooth((wind - kStillAir) / (kMistGone - kStillAir));
    }

    float WindDegrees(double seconds) {
        const double degrees = (SkyOverhaul::Weather::DriftAngle(seconds) + kPi) * 180.0 / kPi;
        return static_cast<float>(std::fmod(degrees, 360.0));
    }

    void AddRainFog(Levers& levers) {
        const float rain = levers.rain;
        if (rain <= 0.0f) {
            return;
        }
        const float harder = Clamp01((rain - kSteadyRain) / (1.0f - kSteadyRain));
        const float end =
            rain < kSteadyRain
                ? Mix(kDrizzleFog, kSteadyRainFog, Clamp01((rain - kDrizzle) / (kSteadyRain - kDrizzle)))
                : Mix(kSteadyRainFog, kDownpourFog, harder);
        const float blue = Smooth(harder);
        const SkyOverhaul::Weather::Fog fog = {
            end,
            {Mix(kRainTint[0], kDownpourTint[0], blue), Mix(kRainTint[1], kDownpourTint[1], blue),
             Mix(kRainTint[2], kDownpourTint[2], blue)},
            0.0f,
            0.0f};
        SkyOverhaul::Weather::AddFog(levers, fog, Smooth(rain / kDrizzle));
    }

    // How much dust is in the air, from the weather's own, the day's, the wind, the sun and the
    // ground under the player; none in fog, mist or rain's air.
    float Dust(const Levers& levers, const SkyOverhaul::Weather::Moment& moment, float wet) {
        const double day = std::floor(moment.seconds / kDay);
        const float dusty = Smooth((Roll(static_cast<long long>(day), 8) - kCrispDays) /
                                   (1.0f - kCrispDays));
        const float lift =
            Mix(kStillDust, 1.0f, Smooth((levers.wind - kCalmWind) / (kDustyWind - kCalmWind)));
        const float daylight = Smooth((moment.sunHeight - 0.05f) / 0.3f);
        const float ground = Clamp01(moment.desert + kSavannahDust * moment.savannah);
        return (std::max)(levers.dust, dusty * lift * daylight) * (1.0f - wet) * ground *
               (1.0f - levers.fogWeight);
    }

    // Nought over the desert, which never rains, one off it.
    float DesertDry(const SkyOverhaul::Weather::Moment& moment) {
        return 1.0f - Smooth((moment.desert - 0.4f) / 0.2f);
    }

    // How far a rainbow can stand in the shower moving off, from `since` the rain here stopped as
    // SinceRain gives it, going again as the last of the rain thickens back toward raining.
    float Bow(float rain, double since) {
        if (since <= 0.0) {
            return 0.0f;
        }
        const float rise = Smooth(static_cast<float>(since / kBowRise));
        const float fade = Smooth(static_cast<float>((since - kBowRise) / kBowFade));
        return rise * (1.0f - fade) * (1.0f - Smooth(rain / kWetRain));
    }

    // What follows from the weather wherever it was read, with `drying` how wet the ground still is
    // from rain that has stopped: no rain over the desert, the ground wetting as rain comes in,
    // rain's fog, dust's, none off wet ground, no heat haze off it either, and gusts.
    void Finish(SkyOverhaul::Weather::Reading& reading, const SkyOverhaul::Weather::Moment& moment,
                float drying) {
        Levers& levers = reading.levers;
        const float dry = DesertDry(moment);
        levers.rain *= dry;
        drying *= dry;
        reading.wet = (std::max)(Smooth(levers.rain / kWetRain), drying);
        AddRainFog(levers);
        reading.dust = Dust(levers, moment, reading.wet);
        const float lifted = Smooth((levers.wind - kBreezeWind) / (kDustyWind - kBreezeWind));
        SkyOverhaul::Weather::AddFog(
            levers,
            {Mix(kLightDust, kThickDust, reading.dust), {kDustTint[0], kDustTint[1], kDustTint[2]},
             lifted, kDustTop},
            Clamp01(reading.dust * 2.5f));
        levers.milk = (std::max)(levers.milk, reading.dust * (1.0f - lifted) * kDustMilk);
        levers.heat *= 1.0f - reading.wet;
        const double t = moment.seconds;
        levers.wind *= 1.0f + 0.15f * static_cast<float>(std::sin(2.0 * kPi * t / 1700.0) *
                                                         std::sin(2.0 * kPi * t / 610.0));
    }
}

SkyOverhaul::Weather::Reading SkyOverhaul::Weather::Read(const Moment& moment) {
    const double t = moment.seconds;
    const AirMass air = AirMassAt(t);
    const Local local = WithCells(air.levers, moment, t, true);
    Reading reading = {local.levers, air.levers, air.row, air.next, air.blend, air.left,
                       0.0f,         0.0f,       0.0f,    WindDegrees(t), local.share, local.distance};
    if (local.row >= 0) {
        reading.row = local.row;
        reading.next = local.row;
        reading.blend = 0.0f;
    }
    Levers& levers = reading.levers;

    const double since = SinceRain(t, levers, moment);
    const float drying = since < 0.0 ? 0.0f : Clamp01(static_cast<float>(1.0 - since / kDrying));
    const float still = Still(levers.wind);

    const float afterRain =
        since > 0.0 ? Smooth(static_cast<float>(since / kMistRising)) *
                          Clamp01(static_cast<float>(1.0 - since / kMistAfterRain))
                    : 0.0f;
    const float rainMist = afterRain * still * Clamp01(moment.jungle + 0.5f * moment.savannah);

    const double day = std::floor(t / kDay);
    const float odds = kJungleMorningFog * moment.jungle + kSavannahMorningFog * moment.savannah;
    const float today = Clamp01((odds - Roll(static_cast<long long>(day), 7)) / 0.1f);
    const float morning = MorningFog(t, moment.sunHeight) * today * still;

    reading.mist = (std::max)(morning, rainMist);
    AddFog(levers, {kMorningFog, {kMistTint[0], kMistTint[1], kMistTint[2]}, 1.0f, kMistTop}, morning);
    AddFog(levers, {kRainMist, {kMistTint[0], kMistTint[1], kMistTint[2]}, 1.0f, kMistTop}, rainMist);
    Finish(reading, moment, drying);
    reading.puddles = Puddles(t, moment) * DesertDry(moment);
    reading.bow = Bow(levers.rain, since) * DesertDry(moment);
    return reading;
}

SkyOverhaul::Weather::Reading SkyOverhaul::Weather::Hold(int row, const Moment& moment) {
    const Levers held = RowLevers(row);
    const Local local = WithCells(held, moment, moment.seconds, false);
    const int showing = local.row >= 0 ? local.row : row;
    Reading reading = {local.levers, held, showing, showing, 0.0f, 0.0, 0.0f, 0.0f, 0.0f,
                       WindDegrees(moment.seconds), local.share, local.distance};
    Finish(reading, moment, 0.0f);
    // A held rain has rained as long as it takes: steady rain and harder brim the puddles. A held
    // clearing has just rained.
    reading.puddles = Clamp01(reading.levers.rain / kSteadyRain);
    static const int s4 = Find("S4");
    reading.bow = showing == s4 ? 1.0f : 0.0f;
    return reading;
}

int SkyOverhaul::Weather::MapCells(const Moment& moment, bool rolled, Cell* out, int capacity) {
    return CellsAt({moment.seconds, moment.world, moment.planted, rolled, moment.x, moment.y, 0.0f},
                   out, capacity);
}

double SkyOverhaul::Weather::DriftAngle(double seconds) {
    return std::fmod(seconds, kWindTurn) / kWindTurn * 2.0 * kPi;
}

void SkyOverhaul::Weather::Drift(double seconds, double speed, double out[2]) {
    const double angle = DriftAngle(seconds);
    const double radius = speed * kWindTurn / (2.0 * kPi);
    out[0] = radius * std::sin(angle);
    out[1] = radius * (1.0 - std::cos(angle));
}

float SkyOverhaul::Weather::Instability(double seconds) {
    const long long segment = static_cast<long long>(std::floor(seconds / kSegment));
    const Family family = PickFamily(Roll(segment, 1));
    const float instability = kFamilyInstability[static_cast<int>(family)];
    if (family != Family::Clear) {
        return instability;
    }
    return instability * (1.0f - kDryAir * Rows()[PickRow(kClear, Roll(segment, 2))].dust);
}
