#include "engine/rain_cover.h"

#include "engine/rain_component.h"
#include "weather/weather.h"

#include <cmath>
#include <cstring>

namespace {
    // Where the engine binds the view-projection for every shader in the frame.
    constexpr UINT kViewProjectionRegister = 4;

    // How far up the rain from a surface its cover is read, so the ground does not cover itself.
    constexpr float kUpRain = 0.4f;

    constexpr D3DFORMAT kNullTarget = static_cast<D3DFORMAT>(MAKEFOURCC('N', 'U', 'L', 'L'));

    // The cover light's square is 64 m across, so its transform scales across by 2/64.
    constexpr float kAcross = 2.0f / 64.0f;
    constexpr float kAcrossTolerance = 0.05f * kAcross;
    // The light stands up the rain from the player, so the player lands near the map's middle.
    constexpr float kOffMiddle = 0.25f;

    SkyOverhaul::RainCover::Found g_found = {};
    // The map's depth surface, which the engine binds again every frame it draws it; null once a
    // frame goes by without it.
    IDirect3DSurface9* g_depth = nullptr;
    uint32_t g_frame = 0;
    bool g_have = false;

    bool IsDepthTexture(IDirect3DSurface9* depth, UINT size, IDirect3DTexture9*& texture) {
        D3DSURFACE_DESC desc = {};
        depth->GetDesc(&desc);
        if ((desc.Format != D3DFMT_D24S8 && desc.Format != D3DFMT_D24X8) || desc.Width != size ||
            desc.Height != size) {
            return false;
        }
        texture = nullptr;
        if (FAILED(depth->GetContainer(__uuidof(IDirect3DTexture9),
                                       reinterpret_cast<void**>(&texture))) ||
            texture == nullptr) {
            return false;
        }
        // The engine holds it for as long as it draws into it; ours is only for the frame.
        texture->Release();
        return true;
    }

    // An orthographic transform 64 m across that puts the player near its middle: the sun's
    // cascades are not square and the sky occlusion's squares lie elsewhere.
    bool IsCoverTransform(const float m[16]) {
        constexpr float kFlat = 1e-5f;
        if (std::fabs(m[12]) > kFlat || std::fabs(m[13]) > kFlat || std::fabs(m[14]) > kFlat ||
            std::fabs(m[15] - 1.0f) > kFlat) {
            return false;
        }
        const float across = std::sqrt(m[0] * m[0] + m[1] * m[1] + m[2] * m[2]);
        if (std::fabs(across - kAcross) > kAcrossTolerance) {
            return false;
        }
        float eye[3];
        if (!SkyOverhaul::Weather::Eye(eye)) {
            return false;
        }
        const float x = m[0] * eye[0] + m[1] * eye[1] + m[2] * eye[2] + m[3];
        const float y = m[4] * eye[0] + m[5] * eye[1] + m[6] * eye[2] + m[7];
        return std::fabs(x) < kOffMiddle && std::fabs(y) < kOffMiddle;
    }
}

void SkyOverhaul::RainCover::OnOffscreenPass(const Frame::Pass& pass) {
    if (pass.frame - g_frame > 1) {
        g_depth = nullptr;
    }
    if (pass.depth == nullptr) {
        return;
    }
    D3DSURFACE_DESC target = {};
    pass.target->GetDesc(&target);
    if (target.Format != kNullTarget || target.Width != target.Height) {
        return;
    }
    IDirect3DTexture9* texture = nullptr;
    if (!IsDepthTexture(pass.depth, target.Width, texture)) {
        return;
    }
    float transform[16];
    if (FAILED(pass.device->GetVertexShaderConstantF(kViewProjectionRegister, transform, 4)) ||
        !IsCoverTransform(transform)) {
        return;
    }
    g_found.texture = texture;
    g_found.size = target.Width;
    std::memcpy(g_found.transform, transform, sizeof(transform));
    g_depth = pass.depth;
    g_frame = pass.frame;
    g_have = true;
}

void SkyOverhaul::RainCover::ForShaders(const Camera::View& view, float transform[16],
                                        float upRain[4]) {
    std::memset(transform, 0, 16 * sizeof(float));
    upRain[0] = 0.0f;
    upRain[1] = 0.0f;
    upRain[2] = kUpRain;
    upRain[3] = 0.0f;
    RainComponent::State state;
    if (RainComponent::Latest(state)) {
        for (int i = 0; i < 3; i++) {
            upRain[i] = -state.direction[i] * kUpRain;
        }
    }
    if (!g_have) {
        transform[3] = 10.0f;
        transform[15] = 1.0f;
        return;
    }
    std::memcpy(transform, g_found.transform, 16 * sizeof(float));
    for (int row = 0; row < 4; row++) {
        const float* r = g_found.transform + row * 4;
        transform[row * 4 + 3] = static_cast<float>(
            static_cast<double>(r[0]) * view.eye[0] + static_cast<double>(r[1]) * view.eye[1] +
            static_cast<double>(r[2]) * view.eye[2] + r[3]);
    }
    upRain[3] = 0.5f / static_cast<float>(g_found.size);
}

bool SkyOverhaul::RainCover::Drawn() {
    return g_depth != nullptr;
}

bool SkyOverhaul::RainCover::Drawing(IDirect3DDevice9* device) {
    if (g_depth == nullptr) {
        return false;
    }
    IDirect3DSurface9* depth = nullptr;
    if (FAILED(device->GetDepthStencilSurface(&depth)) || depth == nullptr) {
        return false;
    }
    depth->Release();
    return depth == g_depth;
}

bool SkyOverhaul::RainCover::Latest(uint32_t frame, Found& out) {
    if (!g_have || g_frame != frame) {
        return false;
    }
    out = g_found;
    return true;
}

bool SkyOverhaul::RainCover::LastSeen(Found& out) {
    if (!g_have) {
        return false;
    }
    out = g_found;
    return true;
}

void SkyOverhaul::RainCover::ReleaseDeviceObjects() {
    g_found = {};
    g_depth = nullptr;
    g_have = false;
}
