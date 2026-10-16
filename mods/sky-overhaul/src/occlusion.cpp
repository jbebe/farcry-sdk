#include "occlusion.h"

#include "depth_constants.h"
#include "tuning.h"

#include "engine/blur_targets.h"
#include "engine/camera.h"
#include "engine/depth_texture.h"
#include "engine/dome_draw.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/solid_depth.h"

#include "occlusion_apply_ps.h"
#include "occlusion_blur_ps.h"
#include "occlusion_gtao_ps.h"

namespace {
    constexpr UINT kFirstConstant = SkyOverhaul::DepthConstants::kFirst;
    constexpr UINT kOwnConstant = kFirstConstant + SkyOverhaul::DepthConstants::kCount;
    constexpr UINT kOwnConstants = 3;
    // Set per blur pass, after the frame's.
    constexpr UINT kBlurConstant = kOwnConstant + kOwnConstants;
    constexpr UINT kFogConstant = kBlurConstant + 1;
    constexpr UINT kFogConstants = 2;

    constexpr DWORD kSolidDepthSampler = 1;

    bool g_enabled = false;

    // The occlusion at the solid depth's size, and the other half of its blur.
    SkyOverhaul::BlurTargets g_halves{"occlusion"};

    SkyOverhaul::PixelShader g_gtaoShader{"occlusion", g_occlusionGtaoPixelShader};
    SkyOverhaul::PixelShader g_blurShader{"occlusion blur", g_occlusionBlurPixelShader};
    SkyOverhaul::PixelShader g_applyShader{"occlusion apply", g_occlusionApplyPixelShader};

    // The occlusion at half resolution and blurred, then multiplied into the world.
    bool Draw(const SkyOverhaul::Frame::Pass& pass, const SkyOverhaul::Camera::View& view,
              const SkyOverhaul::DepthTexture::Found& engine,
              const SkyOverhaul::SolidDepth::Found& solid, const SkyOverhaul::Tuning::Values& v) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* gtaoShader = g_gtaoShader.Get(device);
        IDirect3DPixelShader9* blurShader = g_blurShader.Get(device);
        IDirect3DPixelShader9* applyShader = g_applyShader.Get(device);
        if (gtaoShader == nullptr || blurShader == nullptr || applyShader == nullptr ||
            view.horizontalScale == 0.0f || view.verticalScale == 0.0f ||
            !g_halves.Ensure(device, solid.width, solid.height)) {
            return false;
        }

        // The pixels a metre covers are given for one metre away.
        float projection[2];
        SkyOverhaul::SolidDepth::Projection(view, projection);
        const float own[kOwnConstants * 4] = {
            projection[0], projection[1], 0.0f, v.occlusionRootReach,
            v.occlusionRadius, v.occlusionScreenLimit, v.occlusionFadeDistance, v.occlusionStrength,
            1.0f / static_cast<float>(solid.width), 1.0f / static_cast<float>(solid.height),
            0.5f * view.verticalScale * static_cast<float>(solid.height), 0.0f};

        const float fog[kFogConstants * 4] = {
            view.fogValues[0], view.fogValues[1], view.fogValues[2], view.eye[2],
            view.fogHeightValues[0], view.fogHeightValues[1], view.fogHeightValues[2],
            view.fogHeightValues[3]};

        SkyOverhaul::ScreenDraw draw(device, kFirstConstant,
                                     kFogConstant + kFogConstants - kFirstConstant);
        SkyOverhaul::DepthConstants::Set(device, view, engine, pass.backBuffer.Width,
                                         pass.backBuffer.Height);
        device->SetPixelShaderConstantF(kOwnConstant, own, kOwnConstants);
        device->SetPixelShaderConstantF(kFogConstant, fog, kFogConstants);
        device->SetTexture(kSolidDepthSampler, solid.texture);

        {
            SkyOverhaul::AwayFromPass away(pass);
            device->SetRenderTarget(0, g_halves.First().surface);
            device->SetPixelShader(gtaoShader);
            draw.ClipQuad(0.0f);

            device->SetPixelShader(blurShader);
            g_halves.Blur(device, draw, kBlurConstant);
        }

        // Passes the depth test only where the world drew nearer than the occlusion fades out by.
        // The world target's alpha carries brightness for the bloom, so it is left alone.
        device->SetTexture(0, g_halves.First().texture);
        device->SetRenderState(D3DRS_ZENABLE, D3DZB_TRUE);
        device->SetRenderState(D3DRS_ZFUNC, D3DCMP_GREATER);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_SRCCOLOR);
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);
        device->SetPixelShader(applyShader);
        return draw.ClipQuad(SkyOverhaul::Camera::BufferDepth(view, v.occlusionFadeDistance));
    }
}

void SkyOverhaul::Occlusion::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    const Tuning::Values v = Tuning::Current();
    const bool wanted = v.occlusionStrength > 0.0f;
    SolidDepth::SetWanted(SolidDepth::User::Occlusion, wanted);

    SolidDepth::Found solid = {};
    DepthTexture::Found engine = {};
    Camera::View view;
    if (wanted && SolidDepth::Latest(solid) && DepthTexture::Latest(engine) &&
        Camera::Read(pass.device, view)) {
        Draw(pass, view, engine, solid, v);
    }
}

void SkyOverhaul::Occlusion::ReleaseDeviceObjects() {
    g_halves.Release();
    g_gtaoShader.Release();
    g_blurShader.Release();
    g_applyShader.Release();
}

void SkyOverhaul::Occlusion::SetEnabled(bool enabled) {
    g_enabled = enabled;
    SolidDepth::SetWanted(SolidDepth::User::Occlusion, enabled);
    DomeDraw::SetWatchDepth(DomeDraw::DepthUser::Occlusion, enabled);
}
