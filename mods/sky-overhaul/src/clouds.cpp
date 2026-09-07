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

    // Which frame was last drawn into. A frame can hold more than one pass the sky is drawn in,
    // and drawing into each of them would blend the clouds over themselves.
    uint32_t g_drawnFrame = 0;

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

    void LogPass(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
                 float elapsed) {
        SkyOverhaul::Logf("clouds f%u: eye (%.1f %.1f %.1f) base %.0f | dir (%.2f %.2f %.2f) "
                          "bloom %.2f | %.2f ms",
                          pass.frame, view.eye[0], view.eye[1], view.eye[2], g_baseAltitude,
                          view.direction[0], view.direction[1], view.direction[2], view.bloom,
                          elapsed * 1000.0f);
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
    if (!g_enabled || !pass.sky || !pass.live || pass.frame == g_drawnFrame) {
        return;
    }
    g_drawnFrame = pass.frame;
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
        LogPass(pass, view, elapsed);
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
