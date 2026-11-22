#include "lightning.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/sound.h"
#include "fcse_api.h"
#include "tuning.h"
#include "weather/weather.h"

#include "lightning_bolt_ps.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <vector>

namespace {
    constexpr const char* kChannel = "skyoverhaul";

    constexpr int kMostStrikes = 6;
    // A bolt's main channel and its branches, as points along them: one more than a power of two,
    // which the halving that jags them needs.
    constexpr int kChannelPoints = 65;
    constexpr int kBranches = 3;
    constexpr int kBranchPoints = 17;
    // How many times their turns are rounded, each doubling the points drawn.
    constexpr int kRoundings = 2;
    constexpr int kChannelDrawn = kChannelPoints << kRoundings;
    constexpr int kBranchDrawn = kBranchPoints << kRoundings;

    // Strikes a real second from a cell at its full and at its strongest, and under a stormy air
    // mass at full rain; how far off they still show, and the share that reach the ground.
    constexpr float kCellStrikes = 1.0f / 12.0f;
    constexpr float kStrongStrikes = 3.0f;
    constexpr float kFrontStrikes = 1.0f / 8.0f;
    constexpr float kFarthest = 30000.0f;
    constexpr float kBoltShare = 0.5f;
    // The most strikes a second all the cells off in the distance make together.
    constexpr float kDistantStrikes = 1.0f / 15.0f;
    // Under an air mass's storm, how near and how far round the player strikes land, most of them
    // near; and the rain below which it strikes less, by how much less it rains.
    constexpr float kFrontNearest = 500.0f;
    constexpr float kFrontFarthest = 6000.0f;
    constexpr float kLighterRain = 0.15f;

    // How fast a pulse's light dies away in the bolt and on the land, in seconds, and how long a
    // strike lasts after its last. The land's light reaches the frame a frame or two late through
    // the engine, and a flicker as quick as the bolt's is gone before it shows.
    constexpr float kPulseDecay = 0.045f;
    constexpr float kLandPulseDecay = 0.05f;
    constexpr float kAfterglow = 0.4f;

    // How far below the eye a bolt's foot goes, so ground nearer it hides the rest; how far its foot
    // lands from under its top; how jagged it is, as a share of each piece's length.
    constexpr float kEyeHeight = 1.8f;
    constexpr float kFootSink = 30.0f;
    constexpr float kFootSpread = 250.0f;
    constexpr float kJag = 0.22f;

    // The bolt: its colour, its brightness against the frame's exposure, its channel's width and
    // its glow's, in metres, and the narrowest either is drawn, in pixels.
    constexpr float kBoltColour[3] = {0.82f, 0.88f, 1.0f};
    constexpr float kBoltBrightness = 40.0f;
    constexpr float kChannelWidth = 1.0f;
    constexpr float kGlowWidth = 8.0f;
    constexpr float kNarrowest = 0.8f;
    constexpr float kNarrowestGlow = 2.5f;
    constexpr float kGlowShare = 0.12f;
    constexpr float kBranchShare = 0.45f;
    // Like a tree: the share of the channel's width a branch starts at, tapering to nothing, and the
    // share the channel keeps below each branch it gives off.
    constexpr float kBranchStart = 0.5f;
    constexpr float kAfterBranch = 0.8f;
    // How far into the cloud a bolt starts, as a share of the layer's thickness, and over how many
    // metres below the cloud base it comes out of it.
    constexpr float kInCloud = 0.25f;
    constexpr float kCloudFade = 400.0f;

    // How bright a strike makes the cloud round it.
    constexpr float kCloudGlow = 30.0f;

    // The flash on the world: the light a strike throws at full, in the units of the scene's ambient,
    // the distance at which it has fallen to a half, and the share a flash inside the cloud throws.
    constexpr float kWorldFlash = 2.5f;
    constexpr float kWorldReach = 1000.0f;
    constexpr float kCloudFlash = 0.5f;
    constexpr float kFaintest = 0.01f;

    // Thunder: the banks of a strike near, a few kilometres off and far, where one gives way to the
    // next, the farthest one is heard, and how fast sound travels.
    constexpr uint32_t kCloseThunder = 0x00F90100;
    constexpr uint32_t kRollingThunder = 0x00F90200;
    constexpr uint32_t kFarThunder = 0x00F90300;
    constexpr float kNear = 1200.0f;
    constexpr float kFar = 5000.0f;
    constexpr float kHeard = 15000.0f;
    constexpr float kSoundSpeed = 343.0f;
    constexpr int kMostThunder = 8;

    struct Thunder {
        double due;
        uint32_t id;
    };

    struct Point {
        float x, y, z;
    };

    struct Strike {
        double born;
        // Where it lights the cloud, its bolt's foot height, the cloud base it comes out of, and how
        // far its nearest part is from the eye.
        Point at;
        float ground;
        float base;
        float nearest;
        // Seconds held at full first, for pictures.
        float hold;
        int pulses;
        float pulseAt[3];
        float pulseStrength[3];
        bool bolt;
        Point channel[kChannelDrawn];
        Point branches[kBranches][kBranchDrawn];
        // The channel point each branch leaves from.
        int branchFrom[kBranches];
    };

    struct Asked {
        bool pending;
        bool ahead;
        float degrees;
        float distance;
        int kind;
        float hold;
    };

    struct BoltVertex {
        float x, y, z, rhw;
        float shape[4];
    };

    bool g_enabled = false;
    std::atomic<float> g_worldFlash{0.0f};
    Strike g_strikes[kMostStrikes];
    int g_count = 0;
    double g_now = 0.0;
    SkyOverhaul::Stopwatch g_stopwatch;
    uint32_t g_random = 0x2545F491u;

    std::mutex g_askedLock;
    Asked g_asked = {};

    // Thunder on its way, on a clock both threads read.
    std::mutex g_thunderLock;
    Thunder g_thunder[kMostThunder];
    int g_thunderCount = 0;
    bool g_held = false;

    double Seconds() {
        return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch())
            .count();
    }

    // The thunder of a strike `metres` off, `after` seconds from now plus the time its sound takes to
    // arrive.
    void Rumble(float metres, float after) {
        if (metres > kHeard) {
            return;
        }
        const uint32_t id = metres < kNear ? kCloseThunder : metres < kFar ? kRollingThunder : kFarThunder;
        std::lock_guard<std::mutex> lock(g_thunderLock);
        if (g_thunderCount < kMostThunder) {
            g_thunder[g_thunderCount++] = {Seconds() + after + metres / kSoundSpeed, id};
        }
    }

    SkyOverhaul::PixelShader g_boltShader{"lightning bolt", g_lightningBoltPixelShader};
    std::vector<BoltVertex> g_vertices;

    float Unit() {
        g_random ^= g_random << 13;
        g_random ^= g_random >> 17;
        g_random ^= g_random << 5;
        return static_cast<float>(g_random >> 8) / 16777216.0f;
    }

    float Smooth(float t) {
        t = std::clamp(t, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    // How bright a strike is `age` real seconds after it was born: held, then each pulse flaring and
    // dying away over `decay`. Below nought once it is over.
    float Brightness(const Strike& s, double age, float decay = kPulseDecay) {
        if (age < s.hold) {
            return 1.0f;
        }
        const float t = static_cast<float>(age - s.hold);
        if (t > s.pulseAt[s.pulses - 1] + kAfterglow) {
            return -1.0f;
        }
        float light = 0.0f;
        for (int i = 0; i < s.pulses; i++) {
            if (t >= s.pulseAt[i]) {
                light += s.pulseStrength[i] * std::exp(-(t - s.pulseAt[i]) / decay);
            }
        }
        return (std::min)(light, 1.0f);
    }

    // Jags the line between `points[0]` and `points[count - 1]` by halving it again and again,
    // each middle pushed aside by a share of its piece's length.
    void Jag(Point* points, int count) {
        for (int step = (count - 1) / 2; step >= 1; step /= 2) {
            for (int i = step; i < count - 1; i += 2 * step) {
                const Point& a = points[i - step];
                const Point& b = points[i + step];
                const float dx = b.x - a.x;
                const float dy = b.y - a.y;
                const float dz = b.z - a.z;
                const float length = std::sqrt(dx * dx + dy * dy + dz * dz);
                points[i] = {0.5f * (a.x + b.x) + (Unit() - 0.5f) * length * kJag,
                             0.5f * (a.y + b.y) + (Unit() - 0.5f) * length * kJag,
                             0.5f * (a.z + b.z) + (Unit() - 0.5f) * length * kJag * 0.3f};
            }
        }
    }

    // Rounds a jagged path's turns into `out`, which holds `count << kRoundings` points: each time,
    // every corner is cut a quarter of the way along both its pieces, doubling the points and
    // keeping the ends.
    void Round(const Point* in, int count, Point* out) {
        Point buffers[2][kChannelDrawn];
        const Point* from = in;
        for (int r = 0; r < kRoundings; r++) {
            Point* to = r + 1 == kRoundings ? out : buffers[r % 2];
            to[0] = from[0];
            for (int i = 0; i + 1 < count; i++) {
                const Point& a = from[i];
                const Point& b = from[i + 1];
                to[1 + 2 * i] = {0.75f * a.x + 0.25f * b.x, 0.75f * a.y + 0.25f * b.y,
                                 0.75f * a.z + 0.25f * b.z};
                to[2 + 2 * i] = {0.25f * a.x + 0.75f * b.x, 0.25f * a.y + 0.75f * b.y,
                                 0.25f * a.z + 0.75f * b.z};
            }
            to[2 * count - 1] = from[count - 1];
            from = to;
            count *= 2;
        }
    }

    void Spawn(float x, float y, const float eye[3], float hold, int kind) {
        Strike* s = nullptr;
        if (g_count < kMostStrikes) {
            s = &g_strikes[g_count++];
        } else {
            s = std::min_element(std::begin(g_strikes), std::end(g_strikes),
                                 [](const Strike& a, const Strike& b) { return a.born < b.born; });
        }
        const SkyOverhaul::Tuning::Values v = SkyOverhaul::Tuning::Current();
        s->born = g_now;
        s->at = {x, y, v.cloudBase + 0.35f * v.cloudThickness};
        s->ground = eye[2] - kEyeHeight - kFootSink;
        s->base = v.cloudBase;
        s->hold = hold;
        s->pulses = 1 + static_cast<int>(Unit() * 3.0f) % 3;
        s->pulseAt[0] = 0.0f;
        s->pulseAt[1] = 0.06f + 0.1f * Unit();
        s->pulseAt[2] = s->pulseAt[1] + 0.08f + 0.15f * Unit();
        s->pulseStrength[0] = 1.0f;
        s->pulseStrength[1] = 0.6f + 0.4f * Unit();
        s->pulseStrength[2] = 0.4f + 0.4f * Unit();
        s->bolt = kind < 0 ? Unit() < kBoltShare : kind == 1;
        // The thunder is first heard from the nearest part of the strike: the flash in the cloud,
        // or anywhere down a bolt's channel.
        const auto metres = [eye](const Point& p) {
            const float dx = p.x - eye[0];
            const float dy = p.y - eye[1];
            const float dz = p.z - eye[2];
            return std::sqrt(dx * dx + dy * dy + dz * dz);
        };
        s->nearest = metres(s->at);
        if (!s->bolt) {
            Rumble(s->nearest, hold);
            return;
        }
        Point channel[kChannelPoints];
        channel[0] = {x, y, v.cloudBase + kInCloud * v.cloudThickness};
        channel[kChannelPoints - 1] = {x + (Unit() - 0.5f) * kFootSpread,
                                       y + (Unit() - 0.5f) * kFootSpread, s->ground};
        Jag(channel, kChannelPoints);
        Round(channel, kChannelPoints, s->channel);
        for (const Point& p : s->channel) {
            s->nearest = (std::min)(s->nearest, metres(p));
        }
        Rumble(s->nearest, hold);
        for (int b = 0; b < kBranches; b++) {
            s->branchFrom[b] =
                kChannelDrawn / 5 + static_cast<int>(Unit() * (kChannelDrawn * 2 / 5));
            const Point& from = s->channel[s->branchFrom[b]];
            const float reach = (from.z - s->ground) * (0.2f + 0.25f * Unit());
            const float angle = Unit() * 6.2831853f;
            Point branch[kBranchPoints];
            branch[0] = from;
            branch[kBranchPoints - 1] = {from.x + std::cos(angle) * reach,
                                         from.y + std::sin(angle) * reach, from.z - reach};
            Jag(branch, kBranchPoints);
            Round(branch, kBranchPoints, s->branches[b]);
        }
    }

    // Strikes born this frame: in the raining hearts of mature cells, and round the player under an
    // air mass's own storm.
    void Breed(float seconds, const float eye[3]) {
        static SkyOverhaul::Weather::MapCellList list;
        if (SkyOverhaul::Weather::LatestCells(list)) {
            // A cell over the eye strikes at its own rate; the ones off in the distance share one
            // between them, so the far sky does not flicker with bolts.
            float rates[SkyOverhaul::Weather::kMapCells] = {};
            bool overhead[SkyOverhaul::Weather::kMapCells] = {};
            float distant = 0.0f;
            for (int i = 0; i < list.count; i++) {
                const SkyOverhaul::Weather::Cell& cell = list.cells[i];
                const float dx = cell.x - eye[0];
                const float dy = cell.y - eye[1];
                const float metres = std::sqrt(dx * dx + dy * dy);
                if (cell.mature < 0.5f || metres > kFarthest) {
                    continue;
                }
                rates[i] = kCellStrikes * cell.strength * Smooth((cell.mature - 0.5f) / 0.4f) *
                           (cell.strong ? kStrongStrikes : 1.0f);
                overhead[i] = metres < cell.radius;
                distant += overhead[i] ? 0.0f : rates[i];
            }
            const float share = distant > kDistantStrikes ? kDistantStrikes / distant : 1.0f;
            for (int i = 0; i < list.count; i++) {
                const SkyOverhaul::Weather::Cell& cell = list.cells[i];
                const float rate = rates[i] * (overhead[i] ? 1.0f : share);
                if (rate > 0.0f && Unit() < 1.0f - std::exp(-rate * seconds)) {
                    const float angle = Unit() * 6.2831853f;
                    const float off = std::sqrt(Unit()) * cell.radius * 0.45f;
                    Spawn(cell.x + std::cos(angle) * off, cell.y + std::sin(angle) * off, eye, 0.0f, -1);
                }
            }
        }
        SkyOverhaul::Weather::Reading reading;
        if (SkyOverhaul::Weather::Latest(reading) && reading.cell < 0.5f) {
            // A downpour strikes every eight seconds or so, steady thunderstorm rain about half as
            // often.
            const float rain = reading.levers.rain;
            const float storm = Smooth((reading.levers.storminess - 0.85f) / 0.15f) *
                                Smooth((rain - 0.3f) / 0.3f) *
                                std::clamp((rain - kLighterRain) / (1.0f - kLighterRain), 0.0f, 1.0f);
            if (Unit() < 1.0f - std::exp(-kFrontStrikes * storm * seconds)) {
                const float angle = Unit() * 6.2831853f;
                const float spread = Unit();
                const float off = kFrontNearest + (kFrontFarthest - kFrontNearest) * spread * spread;
                Spawn(eye[0] + std::cos(angle) * off, eye[1] + std::sin(angle) * off, eye, 0.0f, -1);
            }
        }
    }

    // A strike asked for on the channel: `strike <degrees or ahead> <metres> [bolt|cloud] [hold]`.
    void OnMessage(const char*, const void* data, size_t size, void*) {
        char line[128] = {};
        std::memcpy(line, data, (std::min)(size, sizeof(line) - 1));
        char where[16] = {};
        char kind[16] = {};
        Asked asked = {true, false, 0.0f, 0.0f, -1, 0.0f};
        const int read = std::sscanf(line, "strike %15s %f %15s %f", where, &asked.distance, kind,
                                     &asked.hold);
        if (read < 2) {
            FCSE::Logf("lightning: '%s' is not 'strike <degrees or ahead> <metres> [bolt|cloud] [hold]'",
                       line);
            return;
        }
        asked.ahead = std::strcmp(where, "ahead") == 0;
        asked.degrees = asked.ahead ? 0.0f : static_cast<float>(std::atof(where));
        asked.kind = std::strcmp(kind, "bolt") == 0 ? 1 : std::strcmp(kind, "cloud") == 0 ? 0 : -1;
        std::lock_guard<std::mutex> lock(g_askedLock);
        g_asked = asked;
    }

    void TakeAsked(const float eye[3]) {
        Asked asked;
        {
            std::lock_guard<std::mutex> lock(g_askedLock);
            asked = g_asked;
            g_asked.pending = false;
        }
        float degrees = asked.degrees;
        if (!asked.pending || (asked.ahead && !SkyOverhaul::Weather::Facing(degrees))) {
            return;
        }
        const float angle = degrees * 0.0174532925f;
        Spawn(eye[0] + std::cos(angle) * asked.distance, eye[1] + std::sin(angle) * asked.distance,
              eye, asked.hold, asked.kind);
    }

    // The engine's fog over `metres`, as its shaders take it.
    float Fogged(const SkyOverhaul::Camera::View& view, float metres) {
        return std::clamp(metres * view.fogValues[0] + view.fogValues[1], 0.0f, 1.0f) *
               view.fogValues[2];
    }

    struct Projected {
        float x, y, z, w;
    };

    // A point in the world on screen: pixels across and down, depth, and w.
    bool Project(const SkyOverhaul::Camera::View& view, const Point& p, float width, float height,
                 Projected& out) {
        const float* m = view.viewProjection;
        const float x = p.x;
        const float y = p.y;
        const float z = p.z;
        const float cx = m[0] * x + m[1] * y + m[2] * z + m[3];
        const float cy = m[4] * x + m[5] * y + m[6] * z + m[7];
        const float cz = m[8] * x + m[9] * y + m[10] * z + m[11];
        const float cw = m[12] * x + m[13] * y + m[14] * z + m[15];
        if (cw < 1.0f) {
            return false;
        }
        out = {(cx / cw * 0.5f + 0.5f) * width, (0.5f - cy / cw * 0.5f) * height, cz / cw, cw};
        return true;
    }

    // A point of a ribbon on screen: where, which way across it runs there, and how wide it is.
    struct Joint {
        Projected at;
        float across[2];
        float halfWidth;
        float strength;
    };

    // A ribbon along a run of joints, each piece meeting the next on their shared joint.
    void Ribbon(const Joint* joints, int count, float sharpness) {
        for (int i = 0; i + 1 < count; i++) {
            const auto vertex = [&](const Joint& j, float side) {
                return BoltVertex{j.at.x + j.across[0] * side * j.halfWidth - 0.5f,
                                  j.at.y + j.across[1] * side * j.halfWidth - 0.5f,
                                  std::clamp(j.at.z, 0.0f, 1.0f), 1.0f / j.at.w,
                                  {side, j.strength, sharpness, 0.0f}};
            };
            const BoltVertex quad[4] = {vertex(joints[i], -1.0f), vertex(joints[i], 1.0f),
                                        vertex(joints[i + 1], -1.0f), vertex(joints[i + 1], 1.0f)};
            for (int k : {0, 1, 2, 2, 1, 3}) {
                g_vertices.push_back(quad[k]);
            }
        }
    }

    // The channel and its glow along a path, each point at its share of their width and its
    // brightness, coming out of the cloud at `base`; its pieces behind the eye left out.
    void AddPath(const SkyOverhaul::Camera::View& view, const Point* points, int count,
                 const float* widths, const float* lights, float base, float width, float height) {
        const float perMetre = 0.5f * height * view.verticalScale;
        Projected projected[kChannelDrawn];
        bool seen[kChannelDrawn];
        for (int i = 0; i < count; i++) {
            seen[i] = Project(view, points[i], width, height, projected[i]);
        }
        Joint core[kChannelDrawn];
        Joint glow[kChannelDrawn];
        int run = 0;
        for (int i = 0; i <= count; i++) {
            if (i == count || !seen[i]) {
                Ribbon(glow, run, 2.0f);
                Ribbon(core, run, 0.6f);
                run = 0;
                continue;
            }
            const Projected& p = projected[i];
            const Projected& before = i > 0 && seen[i - 1] ? projected[i - 1] : p;
            const Projected& after = i + 1 < count && seen[i + 1] ? projected[i + 1] : p;
            float ax = before.y - after.y;
            float ay = after.x - before.x;
            const float length = (std::max)(std::sqrt(ax * ax + ay * ay), 1e-3f);
            ax /= length;
            ay /= length;
            const float fog = Fogged(view, p.w);
            const float clear = 1.0f - fog;
            const float strength = lights[i] * Smooth((base - points[i].z) / kCloudFade);
            const float wide = widths[i] * perMetre / p.w;
            core[run] = {p, {ax, ay}, (std::max)(kNarrowest, 0.5f * kChannelWidth * wide),
                         strength * (0.1f + 0.9f * clear)};
            glow[run] = {p, {ax, ay},
                         (std::max)(kNarrowestGlow, 0.5f * kGlowWidth * wide) * (1.0f + fog),
                         strength * kGlowShare * (0.5f + 0.5f * clear)};
            run++;
        }
    }

    void DrawBolts(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view) {
        g_vertices.clear();
        const float width = static_cast<float>(pass.viewport.Width);
        const float height = static_cast<float>(pass.viewport.Height);
        for (int i = 0; i < g_count; i++) {
            const Strike& s = g_strikes[i];
            const float light = Brightness(s, g_now - s.born);
            if (!s.bolt || light < kFaintest) {
                continue;
            }
            // The channel keeps its width down to the ground, a step thinner below each branch;
            // a branch starts thinner still and tapers to nothing where it stops in the air.
            float widths[kChannelDrawn];
            float lights[kChannelDrawn];
            for (int p = 0; p < kChannelDrawn; p++) {
                widths[p] = 1.0f;
                for (int from : s.branchFrom) {
                    widths[p] *= p > from ? kAfterBranch : 1.0f;
                }
                lights[p] = light;
            }
            AddPath(view, s.channel, kChannelDrawn, widths, lights, s.base, width, height);
            for (int b = 0; b < kBranches; b++) {
                const float start = kBranchStart * widths[s.branchFrom[b]];
                float branchWidths[kBranchDrawn];
                float branchLights[kBranchDrawn];
                for (int p = 0; p < kBranchDrawn; p++) {
                    const float left = 1.0f - static_cast<float>(p) / (kBranchDrawn - 1);
                    branchWidths[p] = start * left;
                    branchLights[p] = light * kBranchShare * left;
                }
                AddPath(view, s.branches[b], kBranchDrawn, branchWidths, branchLights, s.base,
                        width, height);
            }
        }
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* shader = g_boltShader.Get(device);
        if (g_vertices.empty() || shader == nullptr) {
            return;
        }
        SkyOverhaul::ScreenDraw draw(device, 0, 1);
        const float colour[4] = {kBoltColour[0] * kBoltBrightness * view.bloom,
                                 kBoltColour[1] * kBoltBrightness * view.bloom,
                                 kBoltColour[2] * kBoltBrightness * view.bloom, 0.0f};
        device->SetPixelShaderConstantF(0, colour, 1);
        device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_ONE);
        // The world target's alpha carries brightness for the bloom.
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);
        device->SetPixelShader(shader);
        device->SetFVF(D3DFVF_XYZRHW | D3DFVF_TEX1 | D3DFVF_TEXCOORDSIZE4(0));
        device->DrawPrimitiveUP(D3DPT_TRIANGLELIST, static_cast<UINT>(g_vertices.size() / 3),
                                g_vertices.data(), sizeof(BoltVertex));
    }

    // The light the strikes now throw on the world round the eye, by how near each one's nearest
    // part is.
    float WorldLight() {
        float light = 0.0f;
        for (int i = 0; i < g_count; i++) {
            const Strike& s = g_strikes[i];
            const float b = Brightness(s, g_now - s.born, kLandPulseDecay);
            if (b < kFaintest) {
                continue;
            }
            const float d = s.nearest / kWorldReach;
            light += b * (s.bolt ? 1.0f : kCloudFlash) * kWorldFlash / (1.0f + d * d);
        }
        return light;
    }
}

void SkyOverhaul::Lightning::Install() {
    Sound::Install();
    if (!FCSE::ApiPointer()->Listen(kChannel, &OnMessage, nullptr)) {
        FCSE::Logf("lightning: the %s channel could not be listened on, so no test strikes", kChannel);
    }
}

void SkyOverhaul::Lightning::SetEnabled(bool enabled) {
    g_enabled = enabled;
    if (!enabled) {
        g_count = 0;
    }
}

void SkyOverhaul::Lightning::OnScenePass(const Frame::Pass& pass) {
    if (!pass.live) {
        g_worldFlash.store(0.0f, std::memory_order_relaxed);
        return;
    }
    if (!pass.sky) {
        return;
    }
    const float seconds = (std::min)(g_stopwatch.Lap(), 0.25f);
    g_now += seconds;
    Camera::View view;
    if (!Camera::Read(pass.device, view) || view.verticalScale == 0.0f) {
        return;
    }
    TakeAsked(view.eye);
    if (g_enabled) {
        Breed(seconds, view.eye);
    }
    for (int i = 0; i < g_count;) {
        if (Brightness(g_strikes[i], g_now - g_strikes[i].born) < 0.0f) {
            g_strikes[i] = g_strikes[--g_count];
        } else {
            i++;
        }
    }
    g_worldFlash.store(WorldLight(), std::memory_order_relaxed);
    if (g_count != 0) {
        DrawBolts(pass, view);
    }
}

float SkyOverhaul::Lightning::WorldFlash() {
    return g_worldFlash.load(std::memory_order_relaxed);
}

void SkyOverhaul::Lightning::OnGameFrame() {
    if (!g_held) {
        g_held = true;
        for (uint32_t id : {kCloseThunder, kRollingThunder, kFarThunder}) {
            Sound::Hold(id);
        }
    }
    uint32_t due[kMostThunder];
    int count = 0;
    {
        std::lock_guard<std::mutex> lock(g_thunderLock);
        const double now = Seconds();
        for (int i = 0; i < g_thunderCount;) {
            if (g_thunder[i].due <= now) {
                due[count++] = g_thunder[i].id;
                g_thunder[i] = g_thunder[--g_thunderCount];
            } else {
                i++;
            }
        }
    }
    for (int i = 0; i < count; i++) {
        Sound::Play(due[i], Sound::kEffectNonLocal);
    }
}

void SkyOverhaul::Lightning::Flashes(float out[kFlashes * 4]) {
    std::fill_n(out, kFlashes * 4, 0.0f);
    for (int i = 0; i < g_count; i++) {
        const Strike& s = g_strikes[i];
        const float light = Brightness(s, g_now - s.born) * kCloudGlow;
        // The dimmer of the two kept gives way to a brighter one.
        float* slot = out[3] <= out[7] ? out : out + 4;
        if (light > slot[3]) {
            slot[0] = s.at.x;
            slot[1] = s.at.y;
            slot[2] = s.at.z;
            slot[3] = light;
        }
    }
}

void SkyOverhaul::Lightning::ReleaseDeviceObjects() {
    g_boltShader.Release();
}
