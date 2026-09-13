// A patch drawn where the sun is, with depth testing on and colour writes off, counted by an
// occlusion query - the same way the engine measures its own flare.
//
// The count is read back without stalling, so an answer is a frame or two old. At the rate a sun
// moves behind a wall that is invisible.
#include "engine/sun_occlusion.h"

#include "engine/com.h"
#include "engine/screen_draw.h"

#include <algorithm>

namespace {
    // Half the size of the patch drawn where the sun is, in pixels. Large enough that a thin branch
    // shades part of it rather than all or none, small enough to stay the sun rather than the sky
    // around it.
    constexpr int kHalfSize = 16;

    // Enough measurements in flight that one is always ready without ever waiting on the hardware.
    constexpr int kSlots = 3;

    // Just short of the far plane. Every sky pass draws with depth writes off, so sky pixels keep
    // their cleared far value and the patch survives there; anything the engine drew is nearer and
    // stops it.
    constexpr float kPatchDepth = 0.9999f;

    struct Measurement {
        // Two counts, because with multisampling a query counts samples rather than pixels and the
        // multiplier is not known here. Dividing one by the other cancels it, whatever it is.
        //
        // The first is drawn with depth testing on, the second with it off.
        IDirect3DQuery9* reachedScreen = nullptr;
        IDirect3DQuery9* wouldHaveDrawn = nullptr;
        bool inFlight = false;
    };

    Measurement g_slots[kSlots];
    int g_next = 0;
    IDirect3DDevice9* g_owner = nullptr;
    float g_visibility = -1.0f;

    void ReleaseQueries() {
        for (Measurement& slot : g_slots) {
            SkyOverhaul::Release(slot.reachedScreen);
            SkyOverhaul::Release(slot.wouldHaveDrawn);
            slot.inFlight = false;
        }
    }

    // Queries belong to the device that made them, and a reset invalidates them. Rebuilding when
    // the device changes covers both that and the first call.
    bool EnsureQueries(IDirect3DDevice9* device) {
        if (g_owner == device && g_slots[0].reachedScreen != nullptr) {
            return true;
        }

        ReleaseQueries();
        g_owner = device;
        for (Measurement& slot : g_slots) {
            // The driver does not offer occlusion queries.
            if (FAILED(device->CreateQuery(D3DQUERYTYPE_OCCLUSION, &slot.reachedScreen)) ||
                FAILED(device->CreateQuery(D3DQUERYTYPE_OCCLUSION, &slot.wouldHaveDrawn))) {
                ReleaseQueries();
                return false;
            }
        }
        return true;
    }

    // Reads back anything that has finished. Never waits: an unfinished query is left for a later
    // frame, because blocking here would stall the game on its own render thread.
    void Collect() {
        for (Measurement& slot : g_slots) {
            if (!slot.inFlight) {
                continue;
            }

            DWORD reached = 0;
            DWORD total = 0;
            if (slot.reachedScreen->GetData(&reached, sizeof(reached), 0) != S_OK ||
                slot.wouldHaveDrawn->GetData(&total, sizeof(total), 0) != S_OK) {
                continue;
            }

            slot.inFlight = false;

            // A patch that drew nothing at all measures nothing, and reporting that as "fully
            // occluded" would switch the glare off for a reason that has nothing to do with the
            // sun. Unknown is the honest answer, and callers read it as unoccluded.
            if (total == 0) {
                g_visibility = -1.0f;
                continue;
            }

            const float fraction = static_cast<float>(reached) / static_cast<float>(total);
            g_visibility = fraction > 1.0f ? 1.0f : fraction;
        }
    }
}

void SkyOverhaul::SunOcclusion::Sample(IDirect3DDevice9* device, float centreX, float centreY,
                                       const D3DVIEWPORT9& viewport, CoverFn cover) {
    if (device == nullptr || !EnsureQueries(device)) {
        return;
    }

    Collect();

    Measurement& slot = g_slots[g_next];
    if (slot.inFlight) {
        // Every measurement still outstanding; try again next frame.
        return;
    }

    // Sliding rather than clipping keeps both queries counting the same area, so the ratio stays
    // honest instead of sagging towards the frame's edge.
    const float minX = static_cast<float>(viewport.X) + kHalfSize;
    const float minY = static_cast<float>(viewport.Y) + kHalfSize;
    const float maxX = static_cast<float>(viewport.X + viewport.Width) - kHalfSize;
    const float maxY = static_cast<float>(viewport.Y + viewport.Height) - kHalfSize;
    if (maxX <= minX || maxY <= minY) {
        // A viewport smaller than the patch; nothing to measure against.
        return;
    }

    const float x = std::clamp(centreX, minX, maxX);
    const float y = std::clamp(centreY, minY, maxY);

    const float left = x - kHalfSize;
    const float top = y - kHalfSize;
    const float right = x + kHalfSize;
    const float bottom = y + kHalfSize;

    // No constants of its own: the plain patch is a depth test, and `cover` saves what it writes.
    ScreenDraw draw(device, 0, 0);

    // Measure only; change nothing on screen.
    device->SetRenderState(D3DRS_COLORWRITEENABLE, 0);

    slot.wouldHaveDrawn->Issue(D3DISSUE_BEGIN);
    draw.Quad(left, top, right, bottom, kPatchDepth);
    slot.wouldHaveDrawn->Issue(D3DISSUE_END);

    device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);

    slot.reachedScreen->Issue(D3DISSUE_BEGIN);
    if (!cover(device, left, top, right, bottom, kPatchDepth)) {
        draw.Quad(left, top, right, bottom, kPatchDepth);
    }
    slot.reachedScreen->Issue(D3DISSUE_END);

    slot.inFlight = true;
    g_next = (g_next + 1) % kSlots;
}

float SkyOverhaul::SunOcclusion::Visibility() {
    return g_visibility;
}

void SkyOverhaul::SunOcclusion::ReleaseDeviceObjects() {
    ReleaseQueries();
    g_owner = nullptr;
    g_visibility = -1.0f;
}
