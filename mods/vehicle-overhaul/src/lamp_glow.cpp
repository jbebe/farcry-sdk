#include "lamp_glow.h"

#include "engine/com.h"
#include "engine/handoff.h"
#include "engine/headlights.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/view.h"

#include "lamp_glow_ps.h"

#include <windows.h>

#include <algorithm>
#include <cmath>
#include <iterator>

namespace {
    using VehicleOverhaul::Headlights::kMaxLamps;

    // Half the width of the retail cars' lenses.
    constexpr float kLensRadius = 0.09f;
    // The least a lens is drawn across, in pixels, so a car far off still shows its lamps.
    constexpr float kMinLensPixels = 1.2f;
    // The cosines off the lamp's axis past which the lit lens is gone (70 degrees), and within which
    // it shows whole (55), so it comes in over a short turn rather than lingering faint side on.
    constexpr float kLensGone = 0.342f;
    constexpr float kLensWhole = 0.574f;
    // The occlusion patch: lifted towards the eye so the lamp's own housing does not hide it, and
    // never smaller than this many pixels from its centre.
    constexpr float kPatchLift = 0.25f;
    constexpr float kMinPatchPixels = 2.0f;
    // The glare is whole nearer than the first, in metres, and gone past the second.
    constexpr float kGlareNear = 3.0f;
    constexpr float kGlareFar = 30.0f;
    // The power the cosine off the lamp's axis is raised to, which narrows the glare to its beam.
    constexpr float kBeamTightness = 6.0f;
    // The glare at its whole: its radius as a share of the screen's height, and its brightness.
    constexpr float kGlareRadius = 0.22f;
    constexpr float kGlareBrightness = 0.7f;
    // The faint glow every lit lens keeps, in lens radii, and its brightness.
    constexpr float kHaloLenses = 3.0f;
    constexpr float kHaloBrightness = 0.15f;
    // Halogen's warm white.
    constexpr float kColour[3] = {1.0f, 0.95f, 0.82f};
    // How quickly a lamp's shown visibility follows its measured one, per second.
    constexpr float kRevealRate = 20.0f;
    // Frames a lamp not seen keeps its visibility, so one coming back does not fade in again.
    constexpr uint32_t kForgetFrames = 60;

    // Enough measurements in flight that one is always ready without waiting on the hardware.
    constexpr int kBatches = 3;
    constexpr UINT kConstantRegisters = 3;

    // A lamp placed on screen at the sky pass, drawn at the composite. Both run in that order on the
    // render thread.
    struct Placed {
        uintptr_t id;
        float x;
        float y;
        float halfSize;
        float lens;
        float glareRadius;
        float lensBrightness;
        float glareBrightness;
        // The lamp's axis across the screen, and the cosine the lens is foreshortened by along it.
        float axis[2];
        float foreshortened;
    };

    Placed g_placed[kMaxLamps];
    int g_placedCount = 0;

    // Two counts per lamp, because with multisampling a query counts samples rather than pixels.
    // The first is drawn with the depth test on, the second with it off; their ratio is what shows.
    struct Measurement {
        uintptr_t id;
        IDirect3DQuery9* reached;
        IDirect3DQuery9* total;
        bool pending;
    };

    struct Batch {
        Measurement lamp[kMaxLamps];
        int count;
    };

    Batch g_batches[kBatches] = {};
    int g_nextBatch = 0;
    IDirect3DDevice9* g_owner = nullptr;
    bool g_noQueries = false;

    struct Seen {
        uintptr_t id;
        float measured;
        float shown;
        uint32_t frame;
    };

    Seen g_seen[kMaxLamps * 2];
    int g_seenCount = 0;
    uint32_t g_frame = 0;
    LARGE_INTEGER g_lastComposite = {};

    VehicleOverhaul::PixelShader g_shader{"lamp glow", g_lampGlowPixelShader};

    float Saturate(float value) { return std::clamp(value, 0.0f, 1.0f); }

    Seen* Find(uintptr_t id) {
        Seen* found = std::find_if(g_seen, g_seen + g_seenCount, [id](const Seen& s) { return s.id == id; });
        return found == g_seen + g_seenCount ? nullptr : found;
    }

    Seen& Remember(uintptr_t id) {
        if (Seen* found = Find(id)) {
            return *found;
        }
        if (g_seenCount == static_cast<int>(std::size(g_seen))) {
            Seen* oldest = std::min_element(g_seen, g_seen + g_seenCount,
                                            [](const Seen& a, const Seen& b) { return a.frame < b.frame; });
            *oldest = {id, 0.0f, 0.0f, g_frame};
            return *oldest;
        }
        g_seen[g_seenCount] = {id, 0.0f, 0.0f, g_frame};
        return g_seen[g_seenCount++];
    }

    void ReleaseQueries() {
        for (Batch& batch : g_batches) {
            for (Measurement& m : batch.lamp) {
                VehicleOverhaul::Release(m.reached);
                VehicleOverhaul::Release(m.total);
                m.pending = false;
            }
            batch.count = 0;
        }
    }

    // Queries belong to the device that made them, and a reset invalidates them.
    bool EnsureQueries(IDirect3DDevice9* device) {
        if (g_owner == device && (g_noQueries || g_batches[0].lamp[0].reached != nullptr)) {
            return !g_noQueries;
        }
        ReleaseQueries();
        g_owner = device;
        for (Batch& batch : g_batches) {
            for (Measurement& m : batch.lamp) {
                if (FAILED(device->CreateQuery(D3DQUERYTYPE_OCCLUSION, &m.reached)) ||
                    FAILED(device->CreateQuery(D3DQUERYTYPE_OCCLUSION, &m.total))) {
                    ReleaseQueries();
                    g_noQueries = true;
                    return false;
                }
            }
        }
        return true;
    }

    // Reads back what has finished, never waiting: blocking would stall the render thread.
    void Collect() {
        for (Batch& batch : g_batches) {
            for (int i = 0; i < batch.count; i++) {
                Measurement& m = batch.lamp[i];
                DWORD reached = 0;
                DWORD total = 0;
                if (!m.pending || m.reached->GetData(&reached, sizeof(reached), 0) != S_OK ||
                    m.total->GetData(&total, sizeof(total), 0) != S_OK) {
                    continue;
                }
                m.pending = false;
                // A patch that drew nothing, off the edge of the frame, measures nothing.
                if (total != 0) {
                    Remember(m.id).measured = Saturate(static_cast<float>(reached) / static_cast<float>(total));
                }
            }
        }
    }

    Batch* FreeBatch() {
        Batch& batch = g_batches[g_nextBatch];
        const bool busy = std::any_of(batch.lamp, batch.lamp + batch.count,
                                      [](const Measurement& m) { return m.pending; });
        if (busy) {
            return nullptr;
        }
        batch.count = 0;
        g_nextBatch = (g_nextBatch + 1) % kBatches;
        return &batch;
    }

    struct Clip {
        float x;
        float y;
        float z;
        float w;
    };

    Clip Project(const VehicleOverhaul::View::Camera& camera, const float* point) {
        const float* m = camera.viewProjection;
        const auto row = [&](int r) {
            return m[r * 4] * point[0] + m[r * 4 + 1] * point[1] + m[r * 4 + 2] * point[2] + m[r * 4 + 3];
        };
        return {row(0), row(1), row(2), row(3)};
    }

    void Measure(IDirect3DDevice9* device, VehicleOverhaul::ScreenDraw& draw, Measurement& m, float x,
                 float y, float halfSize, float depth) {
        device->SetRenderState(D3DRS_ZENABLE, D3DZB_FALSE);
        m.total->Issue(D3DISSUE_BEGIN);
        draw.Quad(x - halfSize, y - halfSize, x + halfSize, y + halfSize, depth);
        m.total->Issue(D3DISSUE_END);

        device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        m.reached->Issue(D3DISSUE_BEGIN);
        draw.Quad(x - halfSize, y - halfSize, x + halfSize, y + halfSize, depth);
        m.reached->Issue(D3DISSUE_END);
        m.pending = true;
    }
}

namespace VehicleOverhaul::LampGlow {

void OnSkyPass(const Frame::Pass& pass) {
    g_placedCount = 0;
    g_frame++;
    const Headlights::Snapshot& snapshot = Headlights::Of(Handoff::Drawing());
    View::Camera camera;
    if (snapshot.lamps == 0 || !View::Read(pass.device, camera)) {
        return;
    }

    const bool measuring = EnsureQueries(pass.device);
    if (measuring) {
        Collect();
    }
    Batch* batch = measuring ? FreeBatch() : nullptr;

    const D3DVIEWPORT9& viewport = pass.viewport;
    const float width = static_cast<float>(viewport.Width);
    const float height = static_cast<float>(viewport.Height);
    const float pixelsPerMetre = camera.verticalScale * height * 0.5f;

    ScreenDraw draw(pass.device, 0, 0);
    pass.device->SetRenderState(D3DRS_COLORWRITEENABLE, 0);

    for (int i = 0; i < snapshot.lamps; i++) {
        const Headlights::Lamp& lamp = snapshot.lamp[i];
        float toEye[3];
        for (int a = 0; a < 3; a++) {
            toEye[a] = camera.eye[a] - lamp.position[a];
        }
        const float distance = std::sqrt(toEye[0] * toEye[0] + toEye[1] * toEye[1] + toEye[2] * toEye[2]);
        const float forwardLength = std::sqrt(lamp.forward[0] * lamp.forward[0] + lamp.forward[1] * lamp.forward[1] +
                                              lamp.forward[2] * lamp.forward[2]);
        if (distance < 0.01f || forwardLength < 0.001f) {
            continue;
        }
        const float facing =
            (toEye[0] * lamp.forward[0] + toEye[1] * lamp.forward[1] + toEye[2] * lamp.forward[2]) /
            (distance * forwardLength);
        const float turned = Saturate((facing - kLensGone) / (kLensWhole - kLensGone));
        const float lensShows = turned * turned * (3.0f - 2.0f * turned);
        const Clip clip = Project(camera, lamp.position);
        if (lensShows <= 0.0f || clip.w <= 0.05f) {
            continue;
        }

        Placed& placed = g_placed[g_placedCount++];
        placed.id = lamp.id;
        placed.x = static_cast<float>(viewport.X) + (clip.x / clip.w * 0.5f + 0.5f) * width;
        placed.y = static_cast<float>(viewport.Y) + (0.5f - clip.y / clip.w * 0.5f) * height;
        placed.lens = (std::max)(kLensRadius * pixelsPerMetre / clip.w, kMinLensPixels);

        const float closeness = Saturate((kGlareFar - distance) / (kGlareFar - kGlareNear));
        const float glare = std::pow(Saturate(facing), kBeamTightness) * closeness * closeness;
        const float clear = View::Clear(camera, lamp.position);
        placed.glareRadius = glare * kGlareRadius * height + kHaloLenses * placed.lens;
        placed.halfSize = (std::max)(placed.lens + 2.0f, placed.glareRadius * 2.5f);
        placed.lensBrightness = lensShows * clear;
        placed.glareBrightness = (kHaloBrightness * lensShows + kGlareBrightness * glare) * clear;

        // Where a point a little along the lamp's axis lands, which way that is from the lamp.
        float along[3];
        for (int a = 0; a < 3; a++) {
            along[a] = lamp.position[a] + lamp.forward[a] / forwardLength * 0.1f;
        }
        const Clip tip = Project(camera, along);
        const float axisX = tip.w > 0.05f ? (tip.x / tip.w - clip.x / clip.w) * width : 0.0f;
        const float axisY = tip.w > 0.05f ? (clip.y / clip.w - tip.y / tip.w) * height : 0.0f;
        const float axisLength = std::sqrt(axisX * axisX + axisY * axisY);
        placed.axis[0] = axisLength > 0.0001f ? axisX / axisLength : 1.0f;
        placed.axis[1] = axisLength > 0.0001f ? axisY / axisLength : 0.0f;
        placed.foreshortened = facing;

        Remember(lamp.id).frame = g_frame;
        if (batch == nullptr) {
            continue;
        }
        float lifted[3];
        for (int a = 0; a < 3; a++) {
            lifted[a] = lamp.position[a] + toEye[a] / distance * kPatchLift;
        }
        const Clip patch = Project(camera, lifted);
        if (patch.w <= 0.05f) {
            continue;
        }
        Measurement& m = batch->lamp[batch->count++];
        m.id = lamp.id;
        Measure(pass.device, draw, m, placed.x, placed.y, (std::max)(placed.lens * 0.6f, kMinPatchPixels),
                Saturate(patch.z / patch.w));
    }
}

void OnComposite(const Frame::Pass& pass) {
    LARGE_INTEGER now;
    LARGE_INTEGER frequency;
    QueryPerformanceCounter(&now);
    QueryPerformanceFrequency(&frequency);
    const float seconds = g_lastComposite.QuadPart == 0
                              ? 0.0f
                              : static_cast<float>(now.QuadPart - g_lastComposite.QuadPart) /
                                    static_cast<float>(frequency.QuadPart);
    g_lastComposite = now;

    if (g_owner == pass.device && !g_noQueries) {
        Collect();
    }
    const float step = Saturate(seconds * kRevealRate);
    int kept = 0;
    for (int i = 0; i < g_seenCount; i++) {
        Seen& seen = g_seen[i];
        if (seen.frame + kForgetFrames < g_frame) {
            continue;
        }
        const float target = g_noQueries ? 1.0f : seen.measured;
        seen.shown += (target - seen.shown) * step;
        g_seen[kept++] = seen;
    }
    g_seenCount = kept;

    IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
    if (g_placedCount == 0 || shader == nullptr) {
        return;
    }

    ScreenDraw draw(pass.device, 0, kConstantRegisters);
    pass.device->SetPixelShader(shader);
    pass.device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    pass.device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
    pass.device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_ONE);

    for (int i = 0; i < g_placedCount; i++) {
        const Placed& placed = g_placed[i];
        const Seen* seen = Find(placed.id);
        const float shown = seen != nullptr ? seen->shown : 0.0f;
        if (shown <= 0.001f) {
            continue;
        }
        const float constants[kConstantRegisters * 4] = {
            placed.halfSize, placed.lens,    placed.glareRadius,   placed.lensBrightness * shown,
            kColour[0],      kColour[1],     kColour[2],           placed.glareBrightness * shown,
            placed.axis[0],  placed.axis[1], placed.foreshortened, 0.0f,
        };
        pass.device->SetPixelShaderConstantF(0, constants, kConstantRegisters);
        draw.Quad(placed.x - placed.halfSize, placed.y - placed.halfSize, placed.x + placed.halfSize,
                  placed.y + placed.halfSize);
    }
}

void ReleaseDeviceObjects() {
    ReleaseQueries();
    g_owner = nullptr;
    g_noQueries = false;
    g_shader.Release();
}

}
