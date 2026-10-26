// AI view: what each soldier near the player sees of him, drawn in the world. Off by default; it
// changes nothing in play, but it covers the screen.
#include "engine/ai_sight.h"
#include "engine/world_draw.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>

namespace {
    using DevTools::AiSight::Point;
    using DevTools::AiSight::Sight;
    using DevTools::WorldDraw::Frame;
    using DevTools::WorldDraw::Vertex;

    constexpr float kRadiansPerDegree = 0.0174532925f;
    constexpr float kArcStepDegrees = 3.0f;
    // How far above his feet the sectors lie, clear of the ground they would otherwise flicker in.
    constexpr float kSectorLift = 0.05f;
    constexpr float kViewLength = 3.0f;
    // The visibility bar: how far above his eyes it starts, its height at full visibility, and the
    // half-width of its tick marks.
    constexpr float kBarBase = 0.4f;
    constexpr float kBarHeight = 1.0f;
    constexpr float kTickHalf = 0.1f;
    constexpr float kCrossHalf = 0.3f;

    // Colours as D3DCOLOR, alpha first: a sector's fill and its outline.
    constexpr uint32_t kFocusFill = 0x3040E0FF;
    constexpr uint32_t kFocusEdge = 0x9040E0FF;
    constexpr uint32_t kTestedFill = 0x183060FF;
    constexpr uint32_t kTestedEdge = 0x603060FF;
    constexpr uint32_t kViewColour = 0xFFFFFFFF;
    constexpr uint32_t kUnnoticedColour = 0xFF909090;
    constexpr uint32_t kSensedColour = 0xFFFFE040;
    constexpr uint32_t kSeenColour = 0xFFFF3030;
    constexpr uint32_t kTickColour = 0xFFFFFFFF;
    constexpr uint32_t kBelievedColour = 0xFFFF40FF;

    bool g_enabled = false;
    std::vector<Sight> g_sights;

    Vertex At(const Point& p, uint32_t colour) { return {p[0], p[1], p[2], colour}; }

    Point Raised(Point p, float metres) {
        p[2] += metres;
        return p;
    }

    void Line(Frame& out, const Point& a, const Point& b, uint32_t colour) {
        out.lines.insert(out.lines.end(), {At(a, colour), At(b, colour)});
    }

    // Grey below what he senses, yellow from what he senses, red from what he sees.
    uint32_t VisibilityColour(const Sight& sight) {
        if (sight.visibility >= sight.clear) {
            return kSeenColour;
        }
        return sight.visibility >= sight.fuzzy ? kSensedColour : kUnnoticedColour;
    }

    // A cone as a sector on the ground around `centre`: a translucent fill, its two straight edges
    // and its arc.
    void Sector(Frame& out, const Point& centre, float yaw, float angle, float length,
                uint32_t fill, uint32_t edge) {
        const int steps = static_cast<int>(std::ceil(angle / kArcStepDegrees));
        const float start = yaw - angle * 0.5f * kRadiansPerDegree;
        const float step = angle * kRadiansPerDegree / steps;
        const float stepCos = std::cos(step);
        const float stepSin = std::sin(step);
        float x = std::cos(start) * length;
        float y = std::sin(start) * length;
        Point previous = {centre[0] + x, centre[1] + y, centre[2]};
        Line(out, centre, previous, edge);
        for (int i = 1; i <= steps; ++i) {
            const float turned = x * stepCos - y * stepSin;
            y = x * stepSin + y * stepCos;
            x = turned;
            const Point point = {centre[0] + x, centre[1] + y, centre[2]};
            Line(out, previous, point, edge);
            out.triangles.insert(out.triangles.end(),
                                 {At(centre, fill), At(previous, fill), At(point, fill)});
            previous = point;
        }
        Line(out, centre, previous, edge);
    }

    // A plus of `axes` arms, x then y then z, each `half` to either side of `at`.
    void Plus(Frame& out, const Point& at, float half, int axes, uint32_t colour) {
        for (int axis = 0; axis < axes; ++axis) {
            Point a = at;
            Point b = at;
            a[axis] -= half;
            b[axis] += half;
            Line(out, a, b, colour);
        }
    }

    void Bar(Frame& out, const Sight& sight) {
        const Point base = Raised(sight.eye, kBarBase);
        Line(out, base, Raised(base, kBarHeight * sight.visibility), VisibilityColour(sight));
        for (const float mark : {sight.fuzzy, sight.clear, 1.0f}) {
            Plus(out, Raised(base, kBarHeight * mark), kTickHalf, 2, kTickColour);
        }
    }

    void Draw(Frame& out, const Sight& sight) {
        const float horizontal = std::hypot(sight.view[0], sight.view[1]);
        if (horizontal > 0.0f) {
            const float yaw = std::atan2(sight.view[1], sight.view[0]);
            const Point ground = Raised(sight.feet, kSectorLift);
            if (sight.peripheralAngle > sight.baseFocusAngle) {
                Sector(out, ground, yaw, sight.peripheralAngle, sight.peripheralLength, kTestedFill,
                       kTestedEdge);
            }
            Sector(out, ground, yaw, sight.baseFocusAngle, sight.focusLength, kFocusFill,
                   kFocusEdge);
            const float reach = kViewLength / horizontal;
            const Point ahead = {sight.eye[0] + sight.view[0] * reach,
                                 sight.eye[1] + sight.view[1] * reach, sight.eye[2]};
            Line(out, sight.eye, ahead, kViewColour);
        }
        if (!sight.known) {
            return;
        }
        Line(out, sight.eye, sight.target, VisibilityColour(sight));
        Bar(out, sight);
        Plus(out, sight.believed, kCrossHalf, 3, kBelievedColour);
        if (!sight.plainlySeen) {
            Line(out, sight.eye, sight.believed, kBelievedColour);
        }
    }

    void Fill(Frame& out) {
        DevTools::AiSight::Snapshot(g_sights);
        for (const Sight& sight : g_sights) {
            Draw(out, sight);
        }
    }
}

int GetAiView() { return g_enabled ? 1 : 0; }

void SetAiView(int value) {
    const bool enabled = value != 0;
    if (enabled == g_enabled) {
        return;
    }
    g_enabled = enabled;
    DevTools::WorldDraw::SetFill(enabled ? &Fill : nullptr);
    FCSE::ApiPointer()->Log(enabled ? "ai view: on" : "ai view: off");
}
