#include "clouds.h"

#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/com.h"
#include "engine/log.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "clouds_ps.h"
#include "clouds_vs.h"

#include <cmath>

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 5;

    // The far end of the depth range, where nothing but sky has been drawn: the world's geometry
    // is all nearer, so a less-or-equal test rejects the quad wherever anything stands, at any
    // distance. A value short of one would stop occluding somewhere down the view distance.
    constexpr float kSkyDepth = 1.0f;

    // How wide one cell of the test pattern is, in metres, and how far out it is drawn before
    // fading away over the last of that. Well short of the horizon, where a cell would cover less
    // than a pixel and alias into a shimmer at every step.
    constexpr float kCellSize = 250.0f;
    constexpr float kMaxDistance = 8000.0f;
    constexpr float kFadeDistance = 4000.0f;

    constexpr float kHeartbeatSeconds = 2.0f;

    // Written by the settings callbacks and read while drawing.
    bool g_enabled = false;
    float g_baseAltitude = 1200.0f;

    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DVertexShader9* g_vertexShader = nullptr;
    IDirect3DPixelShader9* g_pixelShader = nullptr;

    // Latched after a failure, so a device that cannot compile the pair is told once rather than
    // asked every frame.
    bool g_refused = false;

    LARGE_INTEGER g_tickFrequency = {};
    LARGE_INTEGER g_lastTick = {};
    float g_sinceHeartbeat = 0.0f;

    float FrameSeconds() {
        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        if (g_tickFrequency.QuadPart == 0 || g_lastTick.QuadPart == 0) {
            g_lastTick = now;
            return 0.0f;
        }
        const float seconds = static_cast<float>(now.QuadPart - g_lastTick.QuadPart) /
                              static_cast<float>(g_tickFrequency.QuadPart);
        g_lastTick = now;
        return seconds < 0.0f ? 0.0f : seconds;
    }

    bool EnsureDeviceObjects(IDirect3DDevice9* device) {
        if (g_owner != device) {
            SkyOverhaul::Release(g_vertexShader);
            SkyOverhaul::Release(g_pixelShader);
            g_owner = device;
            g_refused = false;
        }
        if (g_vertexShader != nullptr && g_pixelShader != nullptr) {
            return true;
        }
        if (g_refused) {
            return false;
        }

        const HRESULT vertex =
            SkyOverhaul::CreateShader(device, g_cloudsVertexShader, &g_vertexShader);
        const HRESULT pixel =
            SkyOverhaul::CreateShader(device, g_cloudsPixelShader, &g_pixelShader);
        if (FAILED(vertex) || FAILED(pixel)) {
            SkyOverhaul::Release(g_vertexShader);
            SkyOverhaul::Release(g_pixelShader);
            g_refused = true;
            SkyOverhaul::Logf("clouds: this device refused the shader pair (0x%08X, 0x%08X), so "
                              "nothing will be drawn",
                              vertex, pixel);
            return false;
        }
        return true;
    }

    // Everything about the pass that a draw into it depends on, which is worth having in the log
    // beside the first frames it was drawn into.
    void LogPass(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view) {
        SkyOverhaul::Logf("clouds: eye (%.2f %.2f %.2f) engine (%.2f %.2f %.2f) off by %.3f | view "
                          "(%.1f %.1f %.1f)",
                          view.eye[0], view.eye[1], view.eye[2], view.position[0],
                          view.position[1], view.position[2],
                          std::sqrt((view.eye[0] - view.position[0]) *
                                        (view.eye[0] - view.position[0]) +
                                    (view.eye[1] - view.position[1]) *
                                        (view.eye[1] - view.position[1]) +
                                    (view.eye[2] - view.position[2]) *
                                        (view.eye[2] - view.position[2])),
                          view.viewPoint[0], view.viewPoint[1], view.viewPoint[2]);
        SkyOverhaul::Logf("clouds: corners (%.2f %.2f %.2f) (%.2f %.2f %.2f) dir (%.2f %.2f %.2f) "
                          "bloom %.3f time %.1f planes %.3f/%.0f",
                          view.corners[0][0], view.corners[0][1], view.corners[0][2],
                          view.corners[3][0], view.corners[3][1], view.corners[3][2],
                          view.direction[0], view.direction[1], view.direction[2], view.bloom,
                          view.time, view.nearPlane, view.farPlane);
        SkyOverhaul::Logf("clouds: fog colour (%.2f %.2f %.2f) range (%.2f %.2f %.2f) values "
                          "(%.4f %.2f %.2f) height (%.4f %.2f %.2f %.2f)",
                          view.fogColour[0], view.fogColour[1], view.fogColour[2],
                          view.fogColourRange[0], view.fogColourRange[1], view.fogColourRange[2],
                          view.fogValues[0], view.fogValues[1], view.fogValues[2],
                          view.fogHeightValues[0], view.fogHeightValues[1],
                          view.fogHeightValues[2], view.fogHeightValues[3]);

        D3DSURFACE_DESC target = {};
        pass.target->GetDesc(&target);
        IDirect3DSurface9* depth = nullptr;
        D3DSURFACE_DESC depthDesc = {};
        if (SUCCEEDED(pass.device->GetDepthStencilSurface(&depth)) && depth != nullptr) {
            depth->GetDesc(&depthDesc);
            depth->Release();
        }
        SkyOverhaul::Logf("clouds: target %ux%u fmt %u ms %u/%u, depth %ux%u fmt %u ms %u/%u",
                          target.Width, target.Height, target.Format, target.MultiSampleType,
                          target.MultiSampleQuality, depthDesc.Width, depthDesc.Height,
                          depthDesc.Format, depthDesc.MultiSampleType,
                          depthDesc.MultiSampleQuality);

        DWORD bias = 0;
        DWORD slope = 0;
        DWORD multisample = 0;
        DWORD mask = 0;
        DWORD srgb = 0;
        DWORD pointSize = 0;
        DWORD tessellation = 0;
        DWORD stencil = 0;
        pass.device->GetRenderState(D3DRS_DEPTHBIAS, &bias);
        pass.device->GetRenderState(D3DRS_SLOPESCALEDEPTHBIAS, &slope);
        pass.device->GetRenderState(D3DRS_MULTISAMPLEANTIALIAS, &multisample);
        pass.device->GetRenderState(D3DRS_MULTISAMPLEMASK, &mask);
        pass.device->GetRenderState(D3DRS_SRGBWRITEENABLE, &srgb);
        pass.device->GetRenderState(D3DRS_POINTSIZE, &pointSize);
        pass.device->GetRenderState(D3DRS_ADAPTIVETESS_Y, &tessellation);
        pass.device->GetRenderState(D3DRS_STENCILENABLE, &stencil);
        SkyOverhaul::Logf("clouds: bias %08X/%08X msaa %u mask %08X srgb %u a2m %08X/%08X "
                          "stencil %u",
                          bias, slope, multisample, mask, srgb, pointSize, tessellation, stencil);
    }

    void Draw(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::CloudLayer::Lighting& lighting) {
        const float constants[kConstantCount * 4] = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            g_baseAltitude, 0.0f, 0.25f, kCellSize,
            lighting.sunColour[0], lighting.sunColour[1], lighting.sunColour[2], 0.0f,
            lighting.ambientColour[0], lighting.ambientColour[1], lighting.ambientColour[2], 0.0f,
            kMaxDistance, kFadeDistance, 0.0f, 0.0f};

        SkyOverhaul::ScreenDraw draw(pass.device, kFirstConstant, kConstantCount);
        pass.device->SetVertexShader(g_vertexShader);
        pass.device->SetPixelShader(g_pixelShader);
        pass.device->SetPixelShaderConstantF(kFirstConstant, constants, kConstantCount);

        // Tested against the world's own depth, which this pass still owns, and blended the way
        // the engine's cloud layer blended: colour already multiplied in, alpha what survives.
        pass.device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        pass.device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        pass.device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
        pass.device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCALPHA);

        draw.ClipQuad(kSkyDepth, view.corners);
    }
}

void SkyOverhaul::Clouds::Install() {
    QueryPerformanceFrequency(&g_tickFrequency);
}

void SkyOverhaul::Clouds::OnScenePass(const Frame::Pass& pass) {
    // Every scene pass arrives here and only one of them is the sky, so the clock is read after
    // the test rather than before it: ticking on all of them would leave the heartbeat measuring
    // the gap between two passes instead of the time between two frames.
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    const float elapsed = FrameSeconds();

    Camera::View view;
    CloudLayer::Lighting lighting;
    if (!Camera::Read(pass.device, view) || !CloudLayer::Latest(lighting) ||
        !EnsureDeviceObjects(pass.device)) {
        return;
    }

    Draw(pass, view, lighting);

    g_sinceHeartbeat += elapsed;
    if (g_sinceHeartbeat >= kHeartbeatSeconds) {
        g_sinceHeartbeat = 0.0f;
        LogPass(pass, view);
    }
}

void SkyOverhaul::Clouds::ReleaseDeviceObjects() {
    Release(g_vertexShader);
    Release(g_pixelShader);
    g_owner = nullptr;
    ScreenDraw::ReleaseDeviceObjects();
}

void SkyOverhaul::Clouds::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void SkyOverhaul::Clouds::SetBaseAltitude(int metres) {
    g_baseAltitude = static_cast<float>(metres);
}
