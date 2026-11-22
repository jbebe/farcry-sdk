#include "ground.h"

#include "blend.h"
#include "depth_constants.h"
#include "wet.h"

#include "engine/camera.h"
#include "engine/depth_texture.h"
#include "engine/dome_draw.h"
#include "engine/frame_copy.h"
#include "engine/noise.h"
#include "engine/rain_cover.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/solid_depth.h"

#include "ground_ps.h"

#include <cmath>

namespace {
    constexpr UINT kFirstConstant = SkyOverhaul::DepthConstants::kFirst;
    constexpr UINT kOwnConstant = kFirstConstant + SkyOverhaul::DepthConstants::kCount;
    constexpr UINT kOwnConstants = 7;
    constexpr UINT kCoverConstant = kOwnConstant + kOwnConstants;
    constexpr UINT kUpRainConstant = kCoverConstant + 4;

    constexpr DWORD kSceneSampler = 0;
    constexpr DWORD kSolidDepthSampler = 1;
    constexpr DWORD kCoverSampler = 2;
    constexpr DWORD kShapeSampler = 4;

    // How far in front of the solid depth grass and leaves stand before they count as such, and
    // how far out the wet reaches, in metres.
    constexpr float kFoliageDepth = 0.15f;
    constexpr float kReach = 150.0f;
    // The sky overhead against the sky low down, and how many times the sky's brightness a pixel
    // has to be to keep its light.
    constexpr float kOverhead = 1.15f;
    constexpr float kLit = 2.0f;
    // The patterns repeat every this many metres.
    constexpr double kRepeat = 256.0;

    // ground.fx's DRIEST.
    constexpr float kDriest = 0.003f;

    bool g_enabled = false;
    SkyOverhaul::PixelShader g_shader{"ground", g_groundPixelShader};

    float Wrap(double value, double period) {
        return static_cast<float>(value - std::floor(value / period) * period);
    }

    void Draw(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::DepthTexture::Found& depth,
              const SkyOverhaul::SolidDepth::Found& solid, float wetness) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* shader = g_shader.Get(device);
        if (shader == nullptr || !SkyOverhaul::Noise::Ensure(device)) {
            return;
        }
        IDirect3DTexture9* scene = SkyOverhaul::FrameCopy::Take(pass);
        if (scene == nullptr) {
            return;
        }

        float projection[2];
        SkyOverhaul::SolidDepth::Projection(view, projection);
        float fogValues[8];
        SkyOverhaul::DepthConstants::Fog(view, fogValues);
        const float* fog = view.fogColour;
        const float own[kOwnConstants * 4] = {
            projection[0], projection[1], kFoliageDepth, kReach,
            wetness, SkyOverhaul::Wet::Rain(), SkyOverhaul::Wet::Puddles(), SkyOverhaul::Wet::Clock(),
            fogValues[0], fogValues[1], fogValues[2], fogValues[3],
            fogValues[4], fogValues[5], fogValues[6], fogValues[7],
            fog[0], fog[1], fog[2], 0.0f,
            fog[0] * kOverhead, fog[1] * kOverhead, fog[2] * kOverhead,
            SkyOverhaul::Luma(fog) * kLit,
            Wrap(view.eye[0], kRepeat), Wrap(view.eye[1], kRepeat),
            1.0f / static_cast<float>(solid.width), 1.0f / static_cast<float>(solid.height)};
        float cover[16];
        float upRain[4];
        SkyOverhaul::RainCover::ForShaders(view, cover, upRain);

        SkyOverhaul::ScreenDraw draw(device, kFirstConstant, kUpRainConstant + 1 - kFirstConstant);
        SkyOverhaul::DepthConstants::Set(device, view, depth, pass.backBuffer.Width,
                                         pass.backBuffer.Height);
        device->SetPixelShaderConstantF(kOwnConstant, own, kOwnConstants);
        device->SetPixelShaderConstantF(kCoverConstant, cover, 4);
        device->SetPixelShaderConstantF(kUpRainConstant, upRain, 1);

        SkyOverhaul::RainCover::Found found;
        device->SetTexture(kSceneSampler, scene);
        device->SetTexture(kSolidDepthSampler, solid.texture);
        device->SetTexture(kCoverSampler,
                           SkyOverhaul::RainCover::LastSeen(found) ? found.texture : nullptr);
        device->SetTexture(kShapeSampler, SkyOverhaul::Noise::Shape());
        draw.Wrap(kShapeSampler);
        // Filtered, a shadow texture's projected read is a soft compare.
        draw.Linear(kCoverSampler);
        draw.Linear(kShapeSampler);
        draw.KeepAlpha();
        device->SetPixelShader(shader);
        draw.ClipQuad(0.0f, view.corners);
    }
}

void SkyOverhaul::Ground::SetEnabled(bool enabled) {
    g_enabled = enabled;
    SolidDepth::SetWanted(SolidDepth::User::Ground, false);
    DomeDraw::SetWatchDepth(DomeDraw::DepthUser::Ground, enabled);
}

void SkyOverhaul::Ground::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    const float wetness = Wet::Wetness();
    const bool wet = wetness >= kDriest;
    // Open ground is told from grass by the depth without it, wanted only while there is wet to draw.
    SolidDepth::SetWanted(SolidDepth::User::Ground, wet);
    if (!wet) {
        return;
    }
    Camera::View view;
    DepthTexture::Found depth = {};
    SolidDepth::Found solid = {};
    if (Camera::Read(pass.device, view) && view.verticalScale != 0.0f &&
        DepthTexture::Latest(depth) && SolidDepth::Latest(solid)) {
        Draw(pass, view, depth, solid, wetness);
    }
}

void SkyOverhaul::Ground::ReleaseDeviceObjects() {
    g_shader.Release();
}
