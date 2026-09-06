// How much of the sun the player can actually see, measured with an occlusion query.
//
// The engine does have a number for this - the flare's own visibility - but it never becomes
// anything reachable. SunOcclusionFactor, the one global that sounds right, is used by exactly one
// shipped shader, to scale water specular, and it reads 1.0 throughout play. So the measurement is
// made here instead, the same way the engine makes its own: draw the sun's disc with depth testing
// on and colour writes off, and count how many pixels survived.
//
// It has to happen while the engine is drawing the sky. That is the only point in the frame where
// the scene's depth buffer is still attached; by the time the frame is finished and the glare is
// drawn, it has been detached, which is why depth-testing the glare itself did nothing.
//
// The count is read back without stalling, so an answer is a frame or two old. At the rate a sun
// moves behind a wall that is invisible.
#include "engine/sun_occlusion.h"

#include "fcse_api.h"

#include <cstdint>
#include <d3d9.h>

namespace {
    constexpr uint32_t kViewProjectionRegister = 4;

    // Half the size of the patch drawn where the sun is, in pixels. Large enough that a thin branch
    // shades part of it rather than all or none, small enough to stay the sun rather than the sky
    // around it.
    constexpr int kHalfSize = 16;

    // Enough measurements in flight that one is always ready without ever waiting on the hardware.
    constexpr int kSlots = 3;

    struct ScreenVertex {
        float x, y, z, rhw;
    };

    constexpr DWORD kScreenVertexFormat = D3DFVF_XYZRHW;

    constexpr D3DRENDERSTATETYPE kRenderStates[] = {
        D3DRS_ZENABLE,          D3DRS_ZWRITEENABLE,      D3DRS_ZFUNC,
        D3DRS_CULLMODE,         D3DRS_LIGHTING,          D3DRS_FOGENABLE,
        D3DRS_STENCILENABLE,    D3DRS_SCISSORTESTENABLE, D3DRS_COLORWRITEENABLE,
        D3DRS_ALPHATESTENABLE,  D3DRS_ALPHABLENDENABLE,
    };

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

    void ReleaseAll() {
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

        ReleaseAll();
        g_owner = device;
        for (Measurement& slot : g_slots) {
            if (FAILED(device->CreateQuery(D3DQUERYTYPE_OCCLUSION, &slot.reachedScreen)) ||
                FAILED(device->CreateQuery(D3DQUERYTYPE_OCCLUSION, &slot.wouldHaveDrawn))) {
                ReleaseAll();
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

    void DrawPatch(IDirect3DDevice9* device, const ScreenVertex quad[4], bool depthTested) {
        device->SetRenderState(D3DRS_ZENABLE, depthTested ? TRUE : FALSE);
        device->DrawPrimitiveUP(D3DPT_TRIANGLESTRIP, 2, quad, sizeof(ScreenVertex));
    }
}

void SkyOverhaul::SunOcclusion::Sample(IDirect3DDevice9* device, const float sun[3]) {
    if (device == nullptr || !EnsureQueries(device)) {
        return;
    }

    Collect();

    Measurement& slot = g_slots[g_next];
    if (slot.inFlight) {
        return; // every measurement still outstanding; try again next frame
    }

    D3DVIEWPORT9 viewport;
    float viewProjection[16] = {};
    if (FAILED(device->GetViewport(&viewport)) ||
        FAILED(device->GetVertexShaderConstantF(kViewProjectionRegister, viewProjection, 4))) {
        return;
    }

    // The sun is a direction rather than a place, so it projects with w = 0.
    const float clipW = viewProjection[12] * sun[0] + viewProjection[13] * sun[1] +
                        viewProjection[14] * sun[2];
    if (clipW <= 0.0001f) {
        g_visibility = 0.0f; // behind the camera
        return;
    }
    const float clipX = viewProjection[0] * sun[0] + viewProjection[1] * sun[1] +
                        viewProjection[2] * sun[2];
    const float clipY = viewProjection[4] * sun[0] + viewProjection[5] * sun[1] +
                        viewProjection[6] * sun[2];

    const float centreX = static_cast<float>(viewport.X) +
                          (clipX / clipW * 0.5f + 0.5f) * static_cast<float>(viewport.Width);
    const float centreY = static_cast<float>(viewport.Y) +
                          (0.5f - clipY / clipW * 0.5f) * static_cast<float>(viewport.Height);

    // Clipped to the viewport, because a patch hanging off the edge would be counted as occluded
    // in one query and not the other, and the ratio would sag as the sun neared the frame's edge.
    const float minX = static_cast<float>(viewport.X);
    const float minY = static_cast<float>(viewport.Y);
    const float maxX = minX + static_cast<float>(viewport.Width);
    const float maxY = minY + static_cast<float>(viewport.Height);

    const float left = centreX - kHalfSize < minX ? minX : centreX - kHalfSize;
    const float top = centreY - kHalfSize < minY ? minY : centreY - kHalfSize;
    const float right = centreX + kHalfSize > maxX ? maxX : centreX + kHalfSize;
    const float bottom = centreY + kHalfSize > maxY ? maxY : centreY + kHalfSize;

    if (right - left < 1.0f || bottom - top < 1.0f) {
        g_visibility = 0.0f; // the sun is off screen, so none of it reaches the player
        return;
    }

    // Just short of the far plane. Every sky pass draws with depth writes off, so sky pixels keep
    // their cleared far value and this survives there; anything the engine drew is nearer and
    // stops it.
    const ScreenVertex quad[4] = {
        {left, top, 0.9999f, 1.0f},
        {right, top, 0.9999f, 1.0f},
        {left, bottom, 0.9999f, 1.0f},
        {right, bottom, 0.9999f, 1.0f},
    };

    DWORD savedRenderState[sizeof(kRenderStates) / sizeof(kRenderStates[0])] = {};
    for (size_t i = 0; i < sizeof(kRenderStates) / sizeof(kRenderStates[0]); i++) {
        device->GetRenderState(kRenderStates[i], &savedRenderState[i]);
    }
    IDirect3DVertexShader9* savedVertexShader = nullptr;
    IDirect3DPixelShader9* savedPixelShader = nullptr;
    DWORD savedFvf = 0;
    device->GetVertexShader(&savedVertexShader);
    device->GetPixelShader(&savedPixelShader);
    device->GetFVF(&savedFvf);

    device->SetVertexShader(nullptr);
    device->SetPixelShader(nullptr);
    device->SetFVF(kScreenVertexFormat);

    device->SetRenderState(D3DRS_ZWRITEENABLE, FALSE);
    device->SetRenderState(D3DRS_ZFUNC, D3DCMP_LESSEQUAL);
    device->SetRenderState(D3DRS_CULLMODE, D3DCULL_NONE);
    device->SetRenderState(D3DRS_LIGHTING, FALSE);
    device->SetRenderState(D3DRS_FOGENABLE, FALSE);
    device->SetRenderState(D3DRS_STENCILENABLE, FALSE);
    device->SetRenderState(D3DRS_SCISSORTESTENABLE, FALSE);
    device->SetRenderState(D3DRS_ALPHATESTENABLE, FALSE);
    device->SetRenderState(D3DRS_ALPHABLENDENABLE, FALSE);
    device->SetRenderState(D3DRS_COLORWRITEENABLE, 0); // measure only; change nothing on screen

    slot.wouldHaveDrawn->Issue(D3DISSUE_BEGIN);
    DrawPatch(device, quad, false);
    slot.wouldHaveDrawn->Issue(D3DISSUE_END);

    slot.reachedScreen->Issue(D3DISSUE_BEGIN);
    DrawPatch(device, quad, true);
    slot.reachedScreen->Issue(D3DISSUE_END);

    slot.inFlight = true;
    g_next = (g_next + 1) % kSlots;

    for (size_t i = 0; i < sizeof(kRenderStates) / sizeof(kRenderStates[0]); i++) {
        device->SetRenderState(kRenderStates[i], savedRenderState[i]);
    }
    device->SetVertexShader(savedVertexShader);
    device->SetPixelShader(savedPixelShader);
    device->SetFVF(savedFvf);

    if (savedVertexShader != nullptr) {
        savedVertexShader->Release();
    }
    if (savedPixelShader != nullptr) {
        savedPixelShader->Release();
    }
}

float SkyOverhaul::SunOcclusion::Visibility() {
    return g_visibility;
}

void SkyOverhaul::SunOcclusion::LastCounts(unsigned long& reachedScreen,
                                           unsigned long& wouldHaveDrawn) {
    reachedScreen = g_lastReached;
    wouldHaveDrawn = g_lastTotal;
}
