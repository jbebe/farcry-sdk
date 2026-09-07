#include "sky.h"

#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/com.h"
#include "engine/dome_draw.h"
#include "engine/log.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "sky_ps.h"
#include "sky_vs.h"

namespace {
    // Above the engine's own globals, which occupy c0 to c64 and would be read back stale by the
    // next draw if a plugin wrote over them. The clouds use the same range: the two never draw in
    // one call, and each puts back what it found.
    constexpr UINT kFirstConstant = 71;
    constexpr UINT kConstantCount = 2;

    // The far end of the depth range, where nothing but sky has been drawn. The dome is drawn with
    // a less-or-equal test against a cleared far plane, so this passes wherever no world stands and
    // is rejected wherever one does.
    constexpr float kSkyDepth = 1.0f;

    constexpr float kHeartbeatSeconds = 5.0f;

    bool g_enabled = false;

    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DVertexShader9* g_vertexShader = nullptr;
    IDirect3DPixelShader9* g_pixelShader = nullptr;

    // Latched after a failure, so a device that cannot compile the pair is told once rather than
    // asked at every dome.
    bool g_refused = false;

    LARGE_INTEGER g_tickFrequency = {};
    LARGE_INTEGER g_lastTick = {};
    float g_sinceHeartbeat = 0.0f;

    float PassSeconds() {
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

        const HRESULT vertex = SkyOverhaul::CreateShader(device, g_skyVertexShader, &g_vertexShader);
        const HRESULT pixel = SkyOverhaul::CreateShader(device, g_skyPixelShader, &g_pixelShader);
        if (FAILED(vertex) || FAILED(pixel)) {
            SkyOverhaul::Release(g_vertexShader);
            SkyOverhaul::Release(g_pixelShader);
            g_refused = true;
            SkyOverhaul::Logf("sky: this device refused the shader pair (0x%08X, 0x%08X), so the "
                              "engine keeps its dome",
                              vertex, pixel);
            return false;
        }
        return true;
    }

    // Runs from inside the dome's own draw call, with everything the engine set for it still bound.
    // Nothing here may change a render state, a sampler or a texture: the sun, the moon and the
    // stars are drawn after this with no blend or depth mode of their own, and inherit whatever the
    // dome left behind.
    bool Draw(IDirect3DDevice9* device) {
        SkyOverhaul::Camera::View view;
        SkyOverhaul::CloudLayer::Lighting lighting;
        if (!SkyOverhaul::Camera::Read(device, view) ||
            !SkyOverhaul::CloudLayer::Latest(lighting) || !EnsureDeviceObjects(device)) {
            return false;
        }

        const float constants[kConstantCount * 4] = {
            view.eye[0], view.eye[1], view.eye[2], view.bloom,
            lighting.sunDirection[0], lighting.sunDirection[1], lighting.sunDirection[2],
            lighting.night};

        SkyOverhaul::DrawGuard guard(device, kFirstConstant, kConstantCount);
        device->SetVertexShader(g_vertexShader);
        device->SetPixelShader(g_pixelShader);
        device->SetPixelShaderConstantF(kFirstConstant, constants, kConstantCount);
        return guard.ClipQuad(kSkyDepth, view.corners);
    }
}

bool SkyOverhaul::Sky::Install() {
    QueryPerformanceFrequency(&g_tickFrequency);
    return DomeDraw::Install(&Draw);
}

void SkyOverhaul::Sky::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    g_sinceHeartbeat += PassSeconds();
    if (g_sinceHeartbeat < kHeartbeatSeconds) {
        return;
    }
    g_sinceHeartbeat = 0.0f;
    // A count that stands still is a dome that stopped being recognised, which is the one way this
    // could fail without anything else saying so.
    Logf("sky f%u: %u domes replaced", pass.frame, DomeDraw::SubstituteCount());
}

void SkyOverhaul::Sky::ReleaseDeviceObjects() {
    Release(g_vertexShader);
    Release(g_pixelShader);
    g_owner = nullptr;
    DrawGuard::ReleaseDeviceObjects();
}

void SkyOverhaul::Sky::SetEnabled(bool enabled) {
    g_enabled = enabled;
    DomeDraw::SetMode(enabled ? DomeDraw::Mode::Overhaul : DomeDraw::Mode::Engine);
}
