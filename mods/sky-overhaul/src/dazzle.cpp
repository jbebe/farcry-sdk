// The glare is computed here rather than in a shader because no shader in the game is given both
// halves of the angle it depends on: the camera is a global, the sun is not.
#include "dazzle.h"

#include "engine/camera.h"
#include "engine/clock.h"
#include "engine/cloud_layer.h"
#include "engine/com.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/sun_occlusion.h"
#include "fcse_api.h"
#include "tuning.h"

#include "dazzle_accumulate_ps.h"
#include "dazzle_bleach_ps.h"
#include "dazzle_ps.h"

#include <algorithm>
#include <cmath>

namespace {

    constexpr float kPi = 3.14159265f;

    constexpr float Radians(float degrees) {
        return degrees * kPi / 180.0f;
    }

    // How wide the glare's own gradient is allowed to get, whatever the spread is set to.
    constexpr float kMaxRadiusRadians = Radians(70.0f);

    // How far past the spread the occlusion query keeps running, so that its answer is already
    // fresh by the time the angle brings it into use.
    constexpr float kSampleMarginRadians = Radians(20.0f);

    // How much longer the eye takes to recover than it spent being dazzled.
    constexpr float kLinger = 2.0f;

    // The sun overstimulates the red and green cones hardest, so the fatigued response first skews
    // towards their complement and then drifts as the cone types recover at different rates.
    //
    // Dark desaturated cyan-green first, dark magenta-purple later.
    constexpr float kCoreEarly[3] = {0.04f, 0.15f, 0.14f};
    constexpr float kCoreLate[3] = {0.13f, 0.05f, 0.15f};

    // An afterimage does not decay smoothly. A slow wander in and out of visibility is most of
    // what sells it as something the eye is doing rather than something drawn on the screen.
    constexpr float kPulseDepth = 0.18f;
    constexpr float kPulseHertz = 1.0f / 3.5f;

    // The image releases as a brief positive before it inverts, over the first of the recovery.
    constexpr float kFlashUntil = 0.06f;
    constexpr float kFlashStrength = 0.45f;

    // How long the eye has to hold on the sun before anything is burned in at all, and how long
    // before the mark is as deep as it gets. A glance across the sun leaves nothing behind.
    constexpr float kArmSeconds = 1.5f;
    constexpr float kFullSeconds = 5.0f;

    // Where the bleached core begins and where it becomes solid within the mask, and the gap
    // between the two is its soft edge. Both sit below the intensity a held stare reaches, so that
    // stare fills the core rather than only approaching it.
    constexpr float kCoreLow = 0.08f;
    constexpr float kCoreHigh = 0.30f;

    // Dazzled above the first, recovering below the second. Two thresholds rather than one so the
    // eye does not flicker in and out at the boundary.
    constexpr float kDazzleOn = 0.15f;
    constexpr float kDazzleOff = 0.10f;

    // A frame longer than this is a hitch rather than time the eye spent looking, and a gap longer
    // than that is a menu or a level load, after which the eye has recovered.
    constexpr float kLongestFrame = 0.1f;
    constexpr float kRecoveryGap = 0.5f;

    // How many pixel shader constant registers the effect writes, from register zero.
    constexpr UINT kConstantRegisters = 6;

    using SkyOverhaul::Tuning::Values;

    // How hard the sun is glaring this frame, and how much of the afterimage that same light is
    // drowning out. The two are separate curves over the same angle.
    struct Glare {
        float intensity;
        float hold;
    };

    // What the recovery looks like this frame, worked out on the way through the state machine.
    struct Recovery {
        float core;
        float surround;
        float flash;
        float haze;
        float colour[3];
    };

    Recovery g_recovery = {};

    bool g_enabled = false;

    // How long the eye has been dazzled and how much of that is left to recover. The afterimage
    // fades over twice as long as it took to build.
    bool g_dazzled = false;
    float g_exposure = 0.0f;
    float g_recovering = 0.0f;
    float g_burnWeight = 0.0f;
    float g_sinceLookAway = 0.0f;
    SkyOverhaul::Stopwatch g_clock;
    SkyOverhaul::Heartbeat g_heartbeat{2.0f};

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

    // A copy of the finished frame to read while overwriting it, and the shader that does the
    // overwriting. Both belong to the device and are surrendered before it is reset.
    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DTexture9* g_sceneCopy = nullptr;
    IDirect3DSurface9* g_sceneCopySurface = nullptr;
    IDirect3DTexture9* g_burn = nullptr;
    IDirect3DSurface9* g_burnSurface = nullptr;
    IDirect3DTexture9* g_bleach = nullptr;
    IDirect3DSurface9* g_bleachSurface = nullptr;
    SkyOverhaul::PixelShader g_shader{"dazzle", g_dazzlePixelShader};
    SkyOverhaul::PixelShader g_accumulateShader{"dazzle accumulate", g_dazzleAccumulatePixelShader};
    SkyOverhaul::PixelShader g_bleachShader{"dazzle bleach", g_dazzleBleachPixelShader};
    D3DSURFACE_DESC g_copyDesc = {};

    // Nothing has been burned in yet, so there is nothing to show. Without this the first
    // afterimage would be whatever the texture's memory happened to hold.
    bool g_burnReady = false;

    // Set when the eye is dazzled afresh, so the burn starts from the current view rather than
    // fading up from what a previous dazzle left behind.
    bool g_burnRestart = false;

    void ReleaseCopy() {
        SkyOverhaul::Release(g_sceneCopySurface);
        SkyOverhaul::Release(g_sceneCopy);
        SkyOverhaul::Release(g_burnSurface);
        SkyOverhaul::Release(g_burn);
        SkyOverhaul::Release(g_bleachSurface);
        SkyOverhaul::Release(g_bleach);
        g_burnReady = false;
    }

    // How a frame is laid into an accumulation target: averaged in by the weight the shader carries
    // in its own alpha, or kept only where it is brighter than what is already there.
    enum class Blend { Mean, Peak };

    // Draws one accumulation pass into `into`, then puts the pass's own target back.
    void AccumulateInto(const SkyOverhaul::Frame::Pass& pass, IDirect3DSurface9* into,
                        IDirect3DPixelShader9* shader, const float* constants, Blend blend) {
        IDirect3DDevice9* device = pass.device;
        if (FAILED(device->SetRenderTarget(0, into))) {
            return;
        }

        {
            SkyOverhaul::ScreenDraw draw(device, 0, kConstantRegisters);
            device->SetPixelShader(shader);
            device->SetTexture(0, g_sceneCopy);

            // A maximum takes the source whole, so it must not be weighted by the alpha the
            // running mean rides on.
            const bool peak = blend == Blend::Peak;
            device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
            device->SetRenderState(D3DRS_BLENDOP, peak ? D3DBLENDOP_MAX : D3DBLENDOP_ADD);
            device->SetRenderState(D3DRS_SRCBLEND, peak ? D3DBLEND_ONE : D3DBLEND_SRCALPHA);
            device->SetRenderState(D3DRS_DESTBLEND, peak ? D3DBLEND_ONE : D3DBLEND_INVSRCALPHA);
            device->SetPixelShaderConstantF(0, constants, kConstantRegisters);

            draw.Quad(0.0f, 0.0f, static_cast<float>(pass.backBuffer.Width),
                      static_cast<float>(pass.backBuffer.Height));
        }

        device->SetRenderTarget(0, pass.target);
        g_burnReady = true;
    }

    HRESULT CreateTarget(IDirect3DDevice9* device, const D3DSURFACE_DESC& desc,
                         IDirect3DTexture9** texture, IDirect3DSurface9** surface) {
        const HRESULT created =
            device->CreateTexture(desc.Width, desc.Height, 1, D3DUSAGE_RENDERTARGET, desc.Format,
                                  D3DPOOL_DEFAULT, texture, nullptr);
        if (FAILED(created)) {
            return created;
        }
        return *texture == nullptr ? E_FAIL : (*texture)->GetSurfaceLevel(0, surface);
    }

    bool EnsureDeviceObjects(IDirect3DDevice9* device, const D3DSURFACE_DESC& backBuffer) {
        if (g_owner != device) {
            ReleaseCopy();
            g_owner = device;
        }

        if (g_shader.Get(device) == nullptr || g_accumulateShader.Get(device) == nullptr ||
            g_bleachShader.Get(device) == nullptr) {
            return false;
        }

        if (g_sceneCopy != nullptr && g_copyDesc.Width == backBuffer.Width &&
            g_copyDesc.Height == backBuffer.Height && g_copyDesc.Format == backBuffer.Format) {
            return true;
        }

        ReleaseCopy();
        const HRESULT copy = CreateTarget(device, backBuffer, &g_sceneCopy, &g_sceneCopySurface);
        const HRESULT burn = CreateTarget(device, backBuffer, &g_burn, &g_burnSurface);
        const HRESULT bleach = CreateTarget(device, backBuffer, &g_bleach, &g_bleachSurface);
        if (FAILED(copy) || FAILED(burn) || FAILED(bleach)) {
            ReleaseCopy();
            FCSE::Logf("dazzle: no %ux%u copy of the frame, 0x%08lX / 0x%08lX / 0x%08lX",
                       backBuffer.Width, backBuffer.Height, static_cast<unsigned long>(copy),
                       static_cast<unsigned long>(burn), static_cast<unsigned long>(bleach));
            return false;
        }

        g_copyDesc = backBuffer;
        FCSE::Logf("dazzle: drawing over %ux%u, format %u", backBuffer.Width, backBuffer.Height,
                   static_cast<unsigned>(backBuffer.Format));
        return true;
    }

    // Carries the eye's exposure forward by one frame and reports how strongly the afterimage
    // should show. It fades over twice as long as the eye spent dazzled, so a short stare leaves a
    // brief mark and a long one leaves a lasting one.
    float AdvanceAfterimage(const Glare& glare, const Values& v, bool live, float elapsed) {
        if (!live || elapsed > kRecoveryGap) {
            g_dazzled = false;
            g_exposure = 0.0f;
            g_recovering = 0.0f;
            return 0.0f;
        }
        if (elapsed > kLongestFrame) {
            elapsed = kLongestFrame;
        }

        const bool dazzled = g_dazzled ? glare.intensity > kDazzleOff : glare.intensity > kDazzleOn;
        if (dazzled) {
            // A look back at the sun adds to what is already on the retina rather than wiping it.
            // Only an eye that has finished recovering starts a fresh image, and that is exactly
            // the eye whose exposure has already been cleared.
            if (g_exposure <= 0.0f) {
                g_sinceLookAway = 0.0f;
                g_burnRestart = true;
            }
            g_exposure += elapsed;
            if (g_exposure > v.afterimageSeconds) {
                g_exposure = v.afterimageSeconds;
            }

            // A running mean over every frame of the stare rather than over the last moment of it,
            // which is what makes the burn a smear of where the eye wandered instead of a snapshot
            // of wherever it happened to stop.
            g_burnWeight = elapsed / g_exposure;

            // Charged at the rate it is spent, and never past what the exposure has paid for. A
            // glance back therefore tops the image up by the length of the glance instead of
            // restoring it whole.
            g_recovering += elapsed;
            if (g_recovering > g_exposure) {
                g_recovering = g_exposure;
            }
        } else {
            // The eye takes longer to recover than it took to dazzle.
            g_recovering -= elapsed / kLinger;
            g_sinceLookAway += elapsed;
            if (g_recovering <= 0.0f) {
                g_recovering = 0.0f;
                g_exposure = 0.0f;
            }
        }
        g_dazzled = dazzled;

        g_recovery = Recovery{};
        if (g_exposure <= 0.0f) {
            return 0.0f;
        }

        // How deep the mark was made, which is not how long ago it happened but how long the eye
        // spent making it. Rooted rather than straight, because bleaching saturates: the first
        // second of a stare costs the eye far more than the fifth.
        const float grown = (g_exposure - kArmSeconds) / (kFullSeconds - kArmSeconds);
        if (grown <= 0.0f) {
            return 0.0f;
        }
        const float maturity = grown >= 1.0f ? 1.0f : std::sqrt(grown);

        // Where the eye is in its recovery, nought the moment the image is released and one when
        // it has cleared.
        const float left = g_recovering / g_exposure;
        const float phase = 1.0f - left;

        // The bleach sits on the retina the whole time. What changes is whether the light coming
        // in right now drowns it out, so it surfaces as the eye turns away rather than appearing
        // all at once at a threshold.
        const float emerging = 1.0f - glare.hold;

        // Everything the recovery shows rides on the same three: how much of it is left, how much
        // of it the light is still drowning out, and how deep it was made in the first place.
        const float presence = left * emerging * maturity;
        const float pulse =
            1.0f + kPulseDepth * std::sin(2.0f * kPi * kPulseHertz * g_sinceLookAway);
        const float envelope = v.afterimageStrength * presence * pulse;

        g_recovery.core = envelope * v.afterimageDarkness;
        g_recovery.surround = envelope * v.afterimageTint;
        g_recovery.haze = v.afterimageStrength * presence * v.afterimageHaze;
        g_recovery.flash = phase < kFlashUntil
                               ? kFlashStrength * (1.0f - phase / kFlashUntil) * emerging * maturity
                               : 0.0f;
        for (int i = 0; i < 3; i++) {
            g_recovery.colour[i] = kCoreEarly[i] + (kCoreLate[i] - kCoreEarly[i]) * phase;
        }

        return envelope;
    }

    // How much of the sun's disc reached the screen. A measurement that could not be made reads as
    // unoccluded, so a silent failure cannot switch the glare off.
    float VisibleFraction() {
        const float measured = SkyOverhaul::SunOcclusion::Visibility();
        return measured < 0.0f ? 1.0f : (measured > 1.0f ? 1.0f : measured);
    }

    // How dazzled the eye is, from nothing to blinded, and how much of that light is drowning the
    // afterimage out.
    Glare Measure(const Values& v) {
        Glare glare = {};

        // Deliberately not gated on the sun being in front of the camera: the whole point is that
        // the angle decides, and a wide spread reaches past a right angle.
        const float spread = Radians(v.glareSpread);
        const float cosAngle = std::clamp(g_thisFrame.cosAngle, -1.0f, 1.0f);
        if (v.glareStrength <= 0.0f || cosAngle <= std::cos(spread)) {
            return glare;
        }

        // Raised to a power rather than eased symmetrically: full strength has to mean the sun in
        // the middle of the view, not merely somewhere on screen, so the curve has to fall away
        // from its peak immediately.
        const float t = std::acos(cosAngle) / spread;
        const float proximity = std::pow(1.0f - t, v.glareFalloff);

        // A low sun is a weak one: its light travels further through the atmosphere.
        const float elevation = std::clamp(
            g_thisFrame.elevation / std::sin(Radians(v.elevationRamp)), 0.0f, 1.0f);

        const float visible = VisibleFraction();
        const float daylight = 1.0f - std::clamp(g_thisFrame.night, 0.0f, 1.0f);

        glare.intensity = v.glareStrength * proximity * elevation * visible * daylight;

        // Light hides the afterimage over a far wider cone than it brightens the frame over, and
        // it hides it completely wherever the sun is anywhere near the middle of the view - a low
        // sun blinds the eye to its own afterimage just as well as a high one, which is why the
        // elevation has no part in this. Only cover releases it early.
        glare.hold = (1.0f - t * t) * visible * daylight;

        return glare;
    }

    // Takes a copy of the finished frame and paints it back through the shader. The copy is needed
    // because the shader reads the same pixels it writes, which no blend state can express.
    void DrawDazzle(const SkyOverhaul::Frame::Pass& pass, const Values& v, float intensity,
                    float afterimage) {
        if (!EnsureDeviceObjects(pass.device, pass.backBuffer)) {
            return;
        }
        if (FAILED(pass.device->StretchRect(pass.target, nullptr, g_sceneCopySurface, nullptr,
                                            D3DTEXF_NONE))) {
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

        const bool restart = g_burnRestart || !g_burnReady;
        const Recovery shown = g_burnReady && afterimage > 0.0f ? g_recovery : Recovery{};
        const float constants[kConstantRegisters * 4] = {
            g_thisFrame.x / width,
            g_thisFrame.y / height,
            intensity,
            v.glareContrast,

            radius,
            width / height,
            v.glareVeil,
            v.glareDesaturation,

            shown.core,
            restart ? 1.0f : g_burnWeight,
            shown.surround,
            shown.flash,

            g_recovery.colour[0],
            g_recovery.colour[1],
            g_recovery.colour[2],
            shown.haze,

            kCoreLow,
            kCoreHigh,
            v.afterimageSaturation,
            0.0f,

            radius * v.afterimageSize,
            0.0f,
            0.0f,
            0.0f};

        // Built up while the eye is dazzled and left alone afterwards, so what fades is what the
        // eye was looking at. Taken from the copy made before the glare is painted on, which is
        // what puts a dark spot where the sun was rather than a white one.
        if (g_dazzled) {
            g_burnRestart = false;
            AccumulateInto(pass, g_burnSurface, g_accumulateShader.Get(pass.device), constants,
                           Blend::Mean);

            // The bleach keeps the most light that fell on each part of the view rather than the
            // average of it: a burned retina does not un-burn when the sun moves on. A mean could
            // not do it anyway, since at a few hundred frames a second its weight rounds to nothing
            // against an eight-bit target.
            AccumulateInto(pass, g_bleachSurface, g_bleachShader.Get(pass.device), constants,
                           restart ? Blend::Mean : Blend::Peak);
        }

        SkyOverhaul::ScreenDraw draw(pass.device, 0, kConstantRegisters);
        pass.device->SetPixelShader(g_shader.Get(pass.device));
        pass.device->SetTexture(0, g_sceneCopy);
        pass.device->SetTexture(1, g_burn);
        pass.device->SetTexture(2, g_bleach);
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
    SunOcclusion::Sample(pass.device, g_thisFrame.x, g_thisFrame.y, pass.viewport);
}

void SkyOverhaul::Dazzle::OnFinalPass(const Frame::Pass& pass) {
    // The composite is the last moment the world owns the frame: the interface is drawn after it,
    // so the glare lands under the heads-up display rather than over it.
    const float elapsed = g_clock.Lap();
    // Switched off reads as a frame with no world in it, which also clears what was burned in.
    const bool live = g_enabled && pass.live;
    const Tuning::Values v = Tuning::Current();
    const Glare glare = live ? Measure(v) : Glare{};
    const float afterimage = AdvanceAfterimage(glare, v, live, elapsed);
    if (glare.intensity > 0.002f || afterimage > 0.002f) {
        DrawDazzle(pass, v, glare.intensity, afterimage);
    }

    if (live && g_heartbeat.Due(elapsed)) {
        FCSE::Logf("dazzle f%u: intensity %.3f hold %.3f | cos %.3f elev %.3f "
                   "night %.2f visible %.3f | exposure %.2f recovering %.2f env %.3f "
                   "| sun (%.0f %.0f) inFront %d",
                   pass.frame, glare.intensity, glare.hold, g_thisFrame.cosAngle,
                   g_thisFrame.elevation, g_thisFrame.night, VisibleFraction(), g_exposure,
                   g_recovering, afterimage, g_thisFrame.x, g_thisFrame.y,
                   g_thisFrame.inFront ? 1 : 0);
    }
}

void SkyOverhaul::Dazzle::ReleaseDeviceObjects() {
    ReleaseCopy();
    g_shader.Release();
    g_accumulateShader.Release();
    g_bleachShader.Release();
    SkyOverhaul::SunOcclusion::ReleaseDeviceObjects();
    g_owner = nullptr;
}

void SkyOverhaul::Dazzle::SetEnabled(bool enabled) {
    g_enabled = enabled;
}
