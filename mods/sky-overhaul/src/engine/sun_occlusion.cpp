// A patch drawn where the sun is, with depth testing on and colour writes off, counted by an
// occlusion query - the same way the engine measures its own flare.
//
// The count is read back without stalling, so an answer is a frame or two old. At the rate a sun
// moves behind a wall that is invisible.
#include "engine/sun_occlusion.h"

#include "engine/screen_draw.h"

#include <cstdint>

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
        IDirect3DQuery9* reachedScreen = nullptr; // depth testing on
        IDirect3DQuery9* wouldHaveDrawn = nullptr; // depth testing off, so the total
        bool inFlight = false;
    };

    Measurement g_slots[kSlots];
    int g_next = 0;
    IDirect3DDevice9* g_owner = nullptr;

    volatile float g_visibility = -1.0f;
    volatile unsigned long g_lastReached = 0;
    volatile unsigned long g_lastTotal = 0;

    void Release() {
        for (Measurement& slot : g_slots) {
            if (slot.reachedScreen != nullptr) {
                slot.reachedScreen->Release();
                slot.reachedScreen = nullptr;
            }
            if (slot.wouldHaveDrawn != nullptr) {
                slot.wouldHaveDrawn->Release();
                slot.wouldHaveDrawn = nullptr;
            }
            slot.inFlight = false;
        }
    }

    // Queries belong to the device that made them, and a reset invalidates them. Rebuilding when
    // the device changes covers both that and the first call.
    bool EnsureQueries(IDirect3DDevice9* device) {
        if (g_owner == device && g_slots[0].reachedScreen != nullptr) {
            return true;
        }

        Release();
        g_owner = device;
        for (Measurement& slot : g_slots) {
            if (FAILED(device->CreateQuery(D3DQUERYTYPE_OCCLUSION, &slot.reachedScreen)) ||
                FAILED(device->CreateQuery(D3DQUERYTYPE_OCCLUSION, &slot.wouldHaveDrawn))) {
                Release();
                return false; // the driver does not offer occlusion queries
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
            g_lastReached = reached;
            g_lastTotal = total;

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
                                       const D3DVIEWPORT9& viewport) {
    if (device == nullptr || !EnsureQueries(device)) {
        return;
    }

    Collect();

    Measurement& slot = g_slots[g_next];
    if (slot.inFlight) {
        return; // every measurement still outstanding; try again next frame
    }

    // The patch is slid back inside the viewport rather than clipped away, so a sun beyond the
    // edge is still measured against the nearest geometry in its direction. The depth buffer holds
    // only what the frustum covers, so this is as close to the sun as anything can be measured -
    // and it is what keeps a wall between the player and an off-screen sun blocking the glare.
    // Sliding rather than clipping also keeps both queries counting the same area, so the ratio
    // stays honest instead of sagging towards the frame's edge.
    const float minX = static_cast<float>(viewport.X) + kHalfSize;
    const float minY = static_cast<float>(viewport.Y) + kHalfSize;
    const float maxX = static_cast<float>(viewport.X + viewport.Width) - kHalfSize;
    const float maxY = static_cast<float>(viewport.Y + viewport.Height) - kHalfSize;
    if (maxX <= minX || maxY <= minY) {
        return; // a viewport smaller than the patch; nothing to measure against
    }

    const float x = centreX < minX ? minX : (centreX > maxX ? maxX : centreX);
    const float y = centreY < minY ? minY : (centreY > maxY ? maxY : centreY);

    const float left = x - kHalfSize;
    const float top = y - kHalfSize;
    const float right = x + kHalfSize;
    const float bottom = y + kHalfSize;

    ScreenDraw draw(device);
    device->SetRenderState(D3DRS_COLORWRITEENABLE, 0); // measure only; change nothing on screen

    slot.wouldHaveDrawn->Issue(D3DISSUE_BEGIN);
    draw.Quad(left, top, right, bottom, kPatchDepth);
    slot.wouldHaveDrawn->Issue(D3DISSUE_END);

    device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);

    slot.reachedScreen->Issue(D3DISSUE_BEGIN);
    draw.Quad(left, top, right, bottom, kPatchDepth);
    slot.reachedScreen->Issue(D3DISSUE_END);

    slot.inFlight = true;
    g_next = (g_next + 1) % kSlots;
}

float SkyOverhaul::SunOcclusion::Visibility() {
    return g_visibility;
}

void SkyOverhaul::SunOcclusion::LastCounts(unsigned long& reachedScreen,
                                           unsigned long& wouldHaveDrawn) {
    reachedScreen = g_lastReached;
    wouldHaveDrawn = g_lastTotal;
}

void SkyOverhaul::SunOcclusion::ReleaseDeviceObjects() {
    Release();
    g_owner = nullptr;
    g_visibility = -1.0f;
}
