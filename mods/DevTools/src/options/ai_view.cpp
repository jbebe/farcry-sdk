// AI view.
//
// Draws what each soldier near the player sees of him, as lines in the world: where his head points,
// his focus cone as his archetype sets it, the wider cone the engine tested this update, a line to
// the player coloured by how visible the player is to him, a bar above his head of the same, and a
// cross where he last perceived the player. Off by default: it changes nothing in play, but it
// covers the screen.
#include "engine/ai_sight.h"
#include "engine/world_lines.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>

namespace {
    using DevTools::AiSight::Sight;
    using DevTools::WorldLines::Vertex;

    constexpr float kRadiansPerDegree = 0.0174532925f;
    constexpr float kArcStepDegrees = 5.0f;
    constexpr float kViewLength = 3.0f;
    // The visibility bar: how far above his eyes it starts, its height at full visibility, and the
    // half-width of its tick marks.
    constexpr float kBarBase = 0.4f;
    constexpr float kBarHeight = 1.0f;
    constexpr float kTickHalf = 0.1f;
    constexpr float kCrossHalf = 0.3f;

    // The visibility lines the game's states and the Difficulty Overhaul read: below 0.3 nothing is
    // noticed, from 0.4 a fighting soldier fires, from 0.5 an idle or alert one recognises an enemy.
    constexpr float kNoticed = 0.3f;
    constexpr float kFires = 0.4f;
    constexpr float kRecognised = 0.5f;

    constexpr uint32_t kViewColour = 0xFFFFFFFF;
    constexpr uint32_t kFocusColour = 0xFF40E0FF;
    constexpr uint32_t kTestedColour = 0xFF3060FF;
    constexpr uint32_t kUnnoticedColour = 0xFF909090;
    constexpr uint32_t kNoticedColour = 0xFFFFE040;
    constexpr uint32_t kFiresColour = 0xFFFF9020;
    constexpr uint32_t kRecognisedColour = 0xFFFF3030;
    constexpr uint32_t kTickColour = 0xFFFFFFFF;
    constexpr uint32_t kBelievedColour = 0xFFFF40FF;

    bool g_enabled = false;

    void Line(std::vector<Vertex>& out, const float a[3], const float b[3], uint32_t colour) {
        out.push_back({a[0], a[1], a[2], colour});
        out.push_back({b[0], b[1], b[2], colour});
    }

    uint32_t VisibilityColour(float visibility) {
        if (visibility >= kRecognised) {
            return kRecognisedColour;
        }
        if (visibility >= kFires) {
            return kFiresColour;
        }
        return visibility >= kNoticed ? kNoticedColour : kUnnoticedColour;
    }

    // A cone as a fan level with his eyes: its two edges and its far arc.
    void Fan(std::vector<Vertex>& out, const Sight& sight, float yaw, float angle, float length,
             uint32_t colour) {
        const float half = angle * 0.5f * kRadiansPerDegree;
        const int steps = static_cast<int>(std::ceil(angle / kArcStepDegrees));
        float previous[3] = {};
        for (int i = 0; i <= steps; ++i) {
            const float a = yaw - half + (2.0f * half) * i / steps;
            const float point[3] = {sight.eye[0] + std::cos(a) * length,
                                    sight.eye[1] + std::sin(a) * length, sight.eye[2]};
            if (i == 0 || i == steps) {
                Line(out, sight.eye, point, colour);
            }
            if (i > 0) {
                Line(out, previous, point, colour);
            }
            previous[0] = point[0];
            previous[1] = point[1];
            previous[2] = point[2];
        }
    }

    // A short horizontal plus at `at`, readable from any side.
    void Tick(std::vector<Vertex>& out, const float at[3], float half, uint32_t colour) {
        const float x0[3] = {at[0] - half, at[1], at[2]};
        const float x1[3] = {at[0] + half, at[1], at[2]};
        const float y0[3] = {at[0], at[1] - half, at[2]};
        const float y1[3] = {at[0], at[1] + half, at[2]};
        Line(out, x0, x1, colour);
        Line(out, y0, y1, colour);
    }

    void Bar(std::vector<Vertex>& out, const Sight& sight) {
        const float base[3] = {sight.eye[0], sight.eye[1], sight.eye[2] + kBarBase};
        const float top[3] = {base[0], base[1], base[2] + kBarHeight * sight.visibility};
        Line(out, base, top, VisibilityColour(sight.visibility));
        for (const float mark : {kNoticed, kFires, kRecognised, 1.0f}) {
            const float at[3] = {base[0], base[1], base[2] + kBarHeight * mark};
            Tick(out, at, kTickHalf, kTickColour);
        }
    }

    void Cross(std::vector<Vertex>& out, const float at[3], uint32_t colour) {
        Tick(out, at, kCrossHalf, colour);
        const float z0[3] = {at[0], at[1], at[2] - kCrossHalf};
        const float z1[3] = {at[0], at[1], at[2] + kCrossHalf};
        Line(out, z0, z1, colour);
    }

    void Draw(std::vector<Vertex>& out, const Sight& sight) {
        const float yaw = std::atan2(sight.view[1], sight.view[0]);
        const float horizontal = std::hypot(sight.view[0], sight.view[1]);
        if (horizontal > 0.0f) {
            const float ahead[3] = {sight.eye[0] + sight.view[0] / horizontal * kViewLength,
                                    sight.eye[1] + sight.view[1] / horizontal * kViewLength,
                                    sight.eye[2]};
            Line(out, sight.eye, ahead, kViewColour);
            Fan(out, sight, yaw, sight.baseFocusAngle, sight.focusLength, kFocusColour);
            if (sight.peripheralAngle > sight.baseFocusAngle) {
                Fan(out, sight, yaw, sight.peripheralAngle, sight.peripheralLength, kTestedColour);
            }
        }
        if (!sight.known) {
            return;
        }
        Line(out, sight.eye, sight.target, VisibilityColour(sight.visibility));
        Bar(out, sight);
        Cross(out, sight.believed, kBelievedColour);
        if (!sight.plainlySeen) {
            Line(out, sight.eye, sight.believed, kBelievedColour);
        }
    }

    void Fill(std::vector<Vertex>& out) {
        for (const Sight& sight : DevTools::AiSight::Snapshot()) {
            Draw(out, sight);
        }
    }
}

int GetAiView() { return g_enabled ? 1 : 0; }

void SetAiView(int value) {
    if (g_enabled == (value != 0)) {
        return;
    }
    g_enabled = value != 0;
    DevTools::AiSight::SetEnabled(g_enabled);
    DevTools::WorldLines::SetEnabled(g_enabled);
    FCSE::ApiPointer()->Log(g_enabled ? "ai view: on" : "ai view: off");
}

void InstallAiViewHooks() {
    DevTools::AiSight::Install();
    DevTools::WorldLines::Install(&Fill);
}
