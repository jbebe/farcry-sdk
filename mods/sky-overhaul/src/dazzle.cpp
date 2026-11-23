// The glare is computed here rather than in a shader because no shader in the game is given both
// halves of the angle it depends on: the camera is a global, the sun is not.
#include "dazzle.h"

#include "angles.h"
#include "clouds.h"
#include "sky_model.h"

#include "engine/camera.h"
#include "engine/cloud_layer.h"
#include "engine/frame_copy.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/sun_occlusion.h"
#include "tuning.h"

#include "dazzle_ps.h"

#include <algorithm>
#include <cmath>

namespace {
    using SkyOverhaul::kPi;
    using SkyOverhaul::Radians;

    // How wide the glare's own gradient is allowed to get, whatever the spread is set to.
    constexpr float kMaxRadiusRadians = Radians(70.0f);

    // How far past the spread the occlusion query keeps running, so that its answer is already
    // fresh by the time the angle brings it into use.
    constexpr float kSampleMarginRadians = Radians(20.0f);

    // The sun's elevation whose glare holds through dusk.
    constexpr float kSettingSun = 5.0f;

    // How many pixel shader constant registers the effect writes, from register zero.
    constexpr UINT kConstantRegisters = 2;

    using SkyOverhaul::Tuning::Values;

    bool g_enabled = false;

    // Where the sun is relative to the view, measured at a scene pass and used at the composite.
    // Both run in that order on one thread, so this needs no synchronisation.
    struct SunInView {
        bool inFront;
        float x;
        float y;
        float cosAngle;
        // One over the tangent of half the vertical field of view, which turns an angle into a
        // distance on screen.
        float verticalScale;
        float elevation;
        float night;
    };

    SunInView g_thisFrame = {};

    // Where the sun lands on screen, from the transform bound right now.
    bool ComputeSun(IDirect3DDevice9* device, const D3DVIEWPORT9& viewport, SunInView& out) {
        out = SunInView{};

        SkyOverhaul::CloudLayer::Lighting lighting;
        if (!SkyOverhaul::CloudLayer::Latest(lighting)) {
            return false;
        }

        SkyOverhaul::Camera::View view;
        if (!SkyOverhaul::Camera::Read(device, view)) {
            return false;
        }
        const float* sun = lighting.sunDirection;
        const float* viewProjection = view.viewProjection;
        const float* camera = view.direction;

        // The sun is a direction rather than a place, so it projects with w = 0: the point on the
        // far plane that direction points at, which is where the engine drew it.
        const float clipW =
            viewProjection[12] * sun[0] + viewProjection[13] * sun[1] + viewProjection[14] * sun[2];
        const float clipX =
            viewProjection[0] * sun[0] + viewProjection[1] * sun[1] + viewProjection[2] * sun[2];
        const float clipY =
            viewProjection[4] * sun[0] + viewProjection[5] * sun[1] + viewProjection[6] * sun[2];

        const float cameraLength = std::sqrt(camera[0] * camera[0] + camera[1] * camera[1] +
                                             camera[2] * camera[2]);

        out.inFront = clipW > 0.0001f;
        out.verticalScale = view.verticalScale;
        out.elevation = sun[2];
        out.night = lighting.night;
        if (cameraLength > 0.0001f) {
            out.cosAngle = (camera[0] * sun[0] + camera[1] * sun[1] + camera[2] * sun[2]) /
                           cameraLength;
        }
        if (out.inFront) {
            out.x = static_cast<float>(viewport.X) +
                    (clipX / clipW * 0.5f + 0.5f) * static_cast<float>(viewport.Width);
            out.y = static_cast<float>(viewport.Y) +
                    (0.5f - clipY / clipW * 0.5f) * static_cast<float>(viewport.Height);
        } else {
            // Behind the camera there is no place on screen to centre the glare on, so it is put
            // far outside the frame and the veil alone carries whatever the angle still calls for.
            out.x = -10.0f * static_cast<float>(viewport.Width);
            out.y = -10.0f * static_cast<float>(viewport.Height);
        }
        return true;
    }

    SkyOverhaul::PixelShader g_shader{"dazzle", g_dazzlePixelShader};

    // How much of the sun's disc reached the screen. A measurement that could not be made reads as
    // unoccluded, so a silent failure cannot switch the glare off.
    float VisibleFraction() {
        const float measured = SkyOverhaul::SunOcclusion::Visibility();
        return measured < 0.0f ? 1.0f : (measured > 1.0f ? 1.0f : measured);
    }

    // How dazzled the eye is, from nothing to blinded.
    float Measure(const Values& v) {
        // Deliberately not gated on the sun being in front of the camera: the whole point is that
        // the angle decides, and a wide spread reaches past a right angle.
        const float spread = Radians(v.glareSpread);
        const float cosAngle = std::clamp(g_thisFrame.cosAngle, -1.0f, 1.0f);
        if (v.glareStrength <= 0.0f || cosAngle <= std::cos(spread)) {
            return 0.0f;
        }

        // Raised to a power rather than eased symmetrically: full strength has to mean the sun in
        // the middle of the view, not merely somewhere on screen, so the curve has to fall away
        // from its peak immediately.
        const float t = std::acos(cosAngle) / spread;
        const float proximity = std::pow(1.0f - t, v.glareFalloff);

        // A low sun is a weak one: its light travels further through the atmosphere. Through dusk
        // it stays as weak as a setting sun's, so the sun shows as a disc.
        const float ramp = std::sin(Radians(v.elevationRamp));
        const float full = std::clamp(g_thisFrame.elevation / ramp, 0.0f, 1.0f);
        const float setting = (std::min)(full, std::sin(Radians(kSettingSun)) / ramp);
        const float elevation =
            full + (setting - full) * SkyOverhaul::SkyModel::Dusk(g_thisFrame.elevation);

        const float daylight = 1.0f - std::clamp(g_thisFrame.night, 0.0f, 1.0f);

        return v.glareStrength * proximity * elevation * VisibleFraction() * daylight;
    }

    // Takes a copy of the finished frame and paints it back through the shader. The copy is needed
    // because the shader reads the same pixels it writes, which no blend state can express.
    void DrawDazzle(const SkyOverhaul::Frame::Pass& pass, const Values& v, float intensity) {
        IDirect3DPixelShader9* shader = g_shader.Get(pass.device);
        if (shader == nullptr) {
            return;
        }
        IDirect3DTexture9* scene = SkyOverhaul::FrameCopy::Take(pass);
        if (scene == nullptr) {
            return;
        }

        const float width = static_cast<float>(pass.backBuffer.Width);
        const float height = static_cast<float>(pass.backBuffer.Height);
        // The vertical half of the frame spans the tangent of half the field of view, so the spread
        // converts into the units the texture coordinates are measured in. Capped short of a right
        // angle, where the tangent runs away; the veil carries a spread wider than the screen.
        const float radius =
            0.5f * std::tan(std::clamp(Radians(v.glareSpread), 0.0f, kMaxRadiusRadians)) *
            g_thisFrame.verticalScale;

        const float constants[kConstantRegisters * 4] = {
            g_thisFrame.x / width,
            g_thisFrame.y / height,
            intensity,
            v.glareContrast,

            radius,
            width / height,
            v.glareVeil,
            v.glareDesaturation};

        SkyOverhaul::ScreenDraw draw(pass.device, 0, kConstantRegisters);
        pass.device->SetPixelShader(shader);
        pass.device->SetTexture(0, scene);
        pass.device->SetPixelShaderConstantF(0, constants, kConstantRegisters);

        draw.Quad(0.0f, 0.0f, width, height);
    }

}

void SkyOverhaul::Dazzle::OnScenePass(const Frame::Pass& pass) {
    if (!g_enabled || !pass.sky || !pass.live) {
        return;
    }
    if (!ComputeSun(pass.device, pass.viewport, g_thisFrame) || !g_thisFrame.inFront) {
        return;
    }

    // Nothing outside the spread can read the answer, and the margin leaves the queries long
    // enough to come back before the angle brings them into use.
    const Tuning::Values v = Tuning::Current();
    const float sampled = std::clamp(Radians(v.glareSpread) + kSampleMarginRadians, 0.0f, kPi);
    if (g_thisFrame.cosAngle <= std::cos(sampled)) {
        return;
    }

    // Measured here rather than at the composite because a depth surface's contents are not
    // guaranteed across being unbound, and the bloom chain between the two unbinds it.
    SunOcclusion::Sample(pass.device, g_thisFrame.x, g_thisFrame.y, pass.viewport,
                         &Clouds::DrawCover);
}

void SkyOverhaul::Dazzle::OnFinalPass(const Frame::Pass& pass) {
    // The composite is the last moment the world owns the frame: the interface is drawn after it,
    // so the glare lands under the heads-up display rather than over it.
    if (!g_enabled || !pass.live) {
        return;
    }
    const Tuning::Values v = Tuning::Current();
    const float intensity = Measure(v);
    if (intensity > 0.002f) {
        DrawDazzle(pass, v, intensity);
    }
}

void SkyOverhaul::Dazzle::ReleaseDeviceObjects() {
    g_shader.Release();
    SkyOverhaul::SunOcclusion::ReleaseDeviceObjects();
}

void SkyOverhaul::Dazzle::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
