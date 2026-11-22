// The weather timeline on its own, without the game: the same moment reads the same, no lever jumps
// between two moments a minute apart, a storm warning always ends in a storm, the families take the
// shares the design log gives them, storms come oftener in the jungle than on the savannah and
// seldom in the desert, a planted storm stands where it was put, no rain or morning fog where the
// rules forbid them, and clouds take the moon's light as far as they cover the sky.
#include "weather/timeline.h"
#include "weather/zones.h"

#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <iterator>

namespace {
    using namespace SkyOverhaul::Weather;

    constexpr double kDay = 86400.0;
    constexpr double kPi = 3.14159265358979;
    constexpr int kDays = 600;
    constexpr Planted kNone = {0.0f, 0.0f, -1.0};

    int g_failures = 0;

    void Check(bool ok, const char* what) {
        if (!ok) {
            std::printf("FAIL %s\n", what);
            g_failures++;
        }
    }

    // A moment at `t` at the middle of world 1, the sun roughly where the clock puts it.
    Moment At(double t, float savannah = 1.0f, float jungle = 0.0f, float desert = 0.0f,
              float x = 2560.0f, float y = 2560.0f) {
        const double hour = std::fmod(t, kDay) / 3600.0;
        const float sun = static_cast<float>(std::sin(2.0 * kPi * (hour - 6.0) / 24.0));
        return {t, sun, savannah, jungle, desert, x, y, 1, kNone};
    }

    // Where the fog ends once laid over the game's own, taken as a noon's 450 m.
    float SeenFog(const Levers& levers) {
        constexpr float kGameFog = 450.0f;
        const float end = FogOf(levers).end;
        return end > 0.0f ? kGameFog + (end - kGameFog) * levers.fogWeight : kGameFog;
    }

    bool Is(int row, const char* id) {
        return std::strcmp(Rows()[row].id, id) == 0;
    }

    int Shown(const Reading& reading) {
        return reading.blend < 0.5f ? reading.row : reading.next;
    }

    // The point of world 1 whose 640 m round it is most of `zone`: 0 savannah, 1 jungle, 2 desert.
    Moment Deepest(int zone, double t) {
        float best = -1.0f;
        float bestX = 0.0f;
        float bestY = 0.0f;
        for (float y = 448.0f; y < 4700.0f; y += 128.0f) {
            for (float x = 448.0f; x < 4700.0f; x += 128.0f) {
                float sum = 0.0f;
                for (float dy = -256.0f; dy <= 256.0f; dy += 128.0f) {
                    for (float dx = -256.0f; dx <= 256.0f; dx += 128.0f) {
                        const Ground g = GroundAt(1, x + dx, y + dy);
                        sum += zone == 0 ? g.savannah : zone == 1 ? g.jungle : g.desert;
                    }
                }
                if (sum > best) {
                    best = sum;
                    bestX = x;
                    bestY = y;
                }
            }
        }
        const Ground g = GroundAt(1, bestX, bestY);
        return At(t, g.savannah, g.jungle, g.desert, bestX, bestY);
    }

    void SameMomentReadsTheSame() {
        for (int i = 0; i < 1000; i++) {
            const Moment moment = At(i * 7919.0, 0.5f, 0.5f);
            const Reading a = Read(moment);
            const Reading b = Read(moment);
            if (std::memcmp(&a.levers, &b.levers, sizeof(Levers)) != 0) {
                Check(false, "the same moment reads the same");
                return;
            }
        }
    }

    void NoJumps() {
        constexpr double kStep = 60.0;
        Reading before = Read(At(0.0, 0.5f, 0.5f));
        float worst = 0.0f;
        float worstFog = 0.0f;
        double worstAt = 0.0;
        for (double t = kStep; t < 100.0 * kDay; t += kStep) {
            const Reading now = Read(At(t, 0.5f, 0.5f));
            const Levers& a = before.levers;
            const Levers& b = now.levers;
            const float jumps[] = {
                std::fabs(a.coverage - b.coverage), std::fabs(a.density - b.density) * 10.0f,
                std::fabs(a.cirrus - b.cirrus),     std::fabs(a.storminess - b.storminess),
                std::fabs(a.rain - b.rain),         std::fabs(a.heat - b.heat),
                std::fabs(a.wind - b.wind) / 250.0f, std::fabs(before.puddles - now.puddles) * 3.0f,
                std::fabs(Cover(a) - Cover(b)),
            };
            for (size_t lever = 0; lever < std::size(jumps); lever++) {
                if (jumps[lever] > worst) {
                    worst = jumps[lever];
                    worstAt = t;
                }
                if (jumps[lever] > 0.15f) {
                    std::printf("lever %zu jumps %.3f at day %.4f: %s>%s %.2f mist %.2f wet %.2f "
                                "cell %.2f -> %s>%s %.2f mist %.2f wet %.2f cell %.2f\n",
                                lever, jumps[lever], t / kDay, Rows()[before.row].id,
                                Rows()[before.next].id, before.blend, before.mist, before.wet,
                                before.cell, Rows()[now.row].id, Rows()[now.next].id, now.blend,
                                now.mist, now.wet, now.cell);
                }
            }
            const float fogA = SeenFog(a);
            const float fogB = SeenFog(b);
            const float fogJump = std::fabs(fogA - fogB) / std::fmin(fogA, fogB);
            if (fogJump > 0.45f) {
                std::printf("fog jumps %.0f m to %.0f m at day %.4f: %s>%s %.2f mist %.2f -> "
                            "%s>%s %.2f mist %.2f\n",
                            fogA, fogB, t / kDay, Rows()[before.row].id, Rows()[before.next].id,
                            before.blend, before.mist, Rows()[now.row].id, Rows()[now.next].id,
                            now.blend, now.mist);
            }
            worstFog = std::fmax(worstFog, fogJump);
            before = now;
        }
        std::printf("largest change in a game minute: %.3f (at day %.2f), fog %.0f%%\n", worst,
                    worstAt / kDay, worstFog * 100.0f);
        // A storm's quickest stage turns a whole lever over in about fifteen game minutes, a gust
        // front sweeps mist off in a few, and the first rain clears dust out of the air as fast.
        Check(worst < 0.15f, "no lever moves more than a seventh in a game minute");
        Check(worstFog < 0.45f, "the fog's end moves less than half in a game minute");
    }

    void WarningEndsInStorm() {
        constexpr double kStep = 300.0;
        int warnings = 0;
        double warnedAt = -1.0;
        bool stormSince = false;
        for (double t = 0.0; t < kDays * kDay; t += kStep) {
            const int row = Shown(Read(At(t)));
            if (Is(row, "F3")) {
                if (warnedAt < 0.0) {
                    warnings++;
                    warnedAt = t;
                    stormSince = false;
                }
            } else if (Is(row, "S1") || Is(row, "S2") || Is(row, "S3")) {
                stormSince = true;
                warnedAt = -1.0;
            } else if (warnedAt >= 0.0) {
                if (!stormSince) {
                    std::printf("warning from day %.4f ended in %s at day %.4f\n", warnedAt / kDay,
                                Rows()[row].id, t / kDay);
                }
                Check(stormSince, "a storm warning ends in a storm");
                warnedAt = -1.0;
            }
        }
        std::printf("storm warnings: %d\n", warnings);
        Check(warnings > 0, "storm warnings happen");
    }

    // Each family's share of the time at the deepest point of a zone.
    void Shares(int zone, double out[5]) {
        constexpr double kStep = 900.0;
        double total = 0.0;
        std::fill(out, out + 5, 0.0);
        Moment moment = Deepest(zone, 0.0);
        for (double t = 0.0; t < kDays * kDay; t += kStep) {
            const double hour = std::fmod(t, kDay) / 3600.0;
            moment.seconds = t;
            moment.sunHeight = static_cast<float>(std::sin(2.0 * kPi * (hour - 6.0) / 24.0));
            out[static_cast<int>(Rows()[Shown(Read(moment))].family)] += kStep;
            total += kStep;
        }
        for (int family = 0; family < 5; family++) {
            out[family] *= 100.0 / total;
        }
    }

    void FamiliesTakeTheirShares() {
        const char* zones[3] = {"savannah", "jungle", "desert"};
        // The design log's storm share per zone.
        const double storms[3] = {15.0, 25.0, 3.0};
        double share[3][5];
        for (int zone = 0; zone < 3; zone++) {
            Shares(zone, share[zone]);
            std::printf("%-8s clear %4.1f%% foggy %4.1f%% overcast %4.1f%% storm %4.1f%% "
                        "(wanted %4.1f%%) wind %4.1f%%\n",
                        zones[zone], share[zone][0], share[zone][1], share[zone][2],
                        share[zone][3], storms[zone], share[zone][4]);
        }
        const int storm = static_cast<int>(Family::Storm);
        Check(share[1][storm] > share[0][storm], "storms come oftener in the jungle");
        Check(share[0][storm] > share[2][storm], "storms come seldom in the desert");
        for (int zone = 0; zone < 3; zone++) {
            Check(std::fabs(share[zone][storm] - storms[zone]) < 6.0, "a zone takes its storms");
        }
    }

    void PlantedStormStands() {
        constexpr double kNoon = 40.0 * kDay + 12.0 * 3600.0;
        Moment moment = At(kNoon);
        moment.planted = {moment.x, moment.y, kNoon};
        const Reading held = Hold(Find("C1"), moment);
        Check(Is(Shown(held), "S2") || Is(Shown(held), "S3"), "a planted storm holds the player");
        Check(held.levers.rain > 0.3f, "a planted storm rains");
        Check(held.sky.coverage == Rows()[Find("C1")].coverage, "the sky away from it stays held");
        Cell cells[8];
        Check(MapCells(moment, false, cells, 8) == 1, "a held row draws only the planted cell");
        moment.planted.x += 30000.0f;
        Check(Is(Shown(Hold(Find("C1"), moment)), "C1"), "a planted storm far off leaves the player");
    }

    void NoRainInTheDesertNorFogInWind() {
        for (double t = 0.0; t < 200.0 * kDay; t += 900.0) {
            const Reading desert = Read(At(t, 0.0f, 0.0f, 1.0f));
            Check(desert.levers.rain == 0.0f, "no rain in the desert");
            Check(desert.mist == 0.0f, "no mist in the desert");
            const Reading jungle = Read(At(t, 0.0f, 1.0f, 0.0f));
            Check(jungle.dust == 0.0f, "no dust in the jungle");
            if (jungle.levers.rain >= 0.3f) {
                Check(FogOf(jungle.levers).end <= 201.0f && jungle.levers.fogWeight > 0.9f,
                      "rain brings its fog");
            }
            if (jungle.mist > 0.0f && jungle.wet == 0.0f) {
                Check(jungle.levers.wind <= 115.0f, "no morning fog in the wind");
            }
            Check(desert.puddles == 0.0f, "no puddles in the desert");
        }
    }

    // Puddles fill through a long rain and are gone a few hours after it.
    void PuddlesFillAndDrain() {
        int filled = 0;
        int lingering = 0;
        for (double t = 6.0 * 3600.0; t < 200.0 * kDay; t += 600.0) {
            const Moment here = At(t, 0.0f, 1.0f, 0.0f);
            bool soaked = true;
            bool dry = true;
            for (double back = 0.0; back <= 4.0 * 3600.0; back += 600.0) {
                const float rain = Read(At(t - back, 0.0f, 1.0f, 0.0f)).levers.rain;
                soaked = soaked && (back > 3600.0 || rain >= 0.6f);
                dry = dry && rain < 0.01f;
            }
            const float puddles = Read(here).puddles;
            if (soaked && puddles < 0.8f) {
                filled++;
            }
            if (dry && puddles > 0.0f) {
                lingering++;
            }
        }
        Check(filled == 0, "an hour of steady rain fills the puddles");
        Check(lingering == 0, "four dry hours empty them");
    }

    // A rainbow can stand just after rain, never while it rains hard, two dry hours on, or over the
    // desert; a held clearing has one.
    void RainbowAfterRain() {
        int bows = 0;
        for (double t = 0.0; t < 200.0 * kDay; t += 600.0) {
            const Reading here = Read(At(t, 0.0f, 1.0f, 0.0f));
            if (here.levers.rain >= 0.15f) {
                Check(here.bow == 0.0f, "no rainbow while it rains");
            }
            bool dry = true;
            for (double back = 0.0; back <= 2.0 * 3600.0 && dry; back += 600.0) {
                dry = Read(At(t - back, 0.0f, 1.0f, 0.0f)).levers.rain < 0.01f;
            }
            if (dry) {
                Check(here.bow == 0.0f, "no rainbow two dry hours on");
            }
            bows += here.bow > 0.5f && here.levers.rain < 0.15f ? 1 : 0;
            Check(Read(At(t, 0.0f, 0.0f, 1.0f)).bow == 0.0f, "no rainbow over the desert");
        }
        std::printf("moments after rain that can hold a rainbow: %d\n", bows);
        Check(bows > 0, "rainbows happen after rain");
        Check(Hold(Find("S4"), At(40.0 * kDay)).bow == 1.0f, "a held clearing holds a rainbow");
    }

    // A clear sky leaves the moon's light, lots of cumulus takes some, overcast and storms nearly all.
    void CloudsTakeTheMoon() {
        const auto cover = [](const char* id) { return Cover(FromRow(Rows()[Find(id)])); };
        Check(cover("C1") == 0.0f, "a clear sky keeps the moonlight");
        Check(cover("C5") > 0.3f && cover("C5") < 0.8f, "lots of cumulus takes some moonlight");
        Check(cover("O3") >= 0.9f && cover("S3") >= 0.9f, "overcast and storms take nearly all");
    }

    void DustOnSomeDays() {
        int dusty = 0;
        constexpr int kDaysChecked = 200;
        for (int day = 0; day < kDaysChecked; day++) {
            const Reading noon = Read(At(day * kDay + 13.0 * 3600.0, 0.0f, 0.0f, 1.0f));
            if (noon.dust > 0.2f && noon.wet == 0.0f) {
                dusty++;
            }
        }
        std::printf("dusty desert noons: %d of %d\n", dusty, kDaysChecked);
        Check(dusty > kDaysChecked / 5 && dusty < kDaysChecked * 4 / 5, "dust comes on some days");
    }
}

int main() {
    SameMomentReadsTheSame();
    NoJumps();
    WarningEndsInStorm();
    FamiliesTakeTheirShares();
    PlantedStormStands();
    DustOnSomeDays();
    NoRainInTheDesertNorFogInWind();
    PuddlesFillAndDrain();
    RainbowAfterRain();
    CloudsTakeTheMoon();
    std::printf(g_failures == 0 ? "weather: OK\n" : "weather: %d failed\n", g_failures);
    return g_failures == 0 ? 0 : 1;
}
