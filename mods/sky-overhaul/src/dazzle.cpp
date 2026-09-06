// The glare is computed here rather than in a shader because no shader in the game is given both
// halves of the angle it depends on: the camera is a global, the sun is not.
#include "dazzle.h"

#include "engine/frame.h"
#include "engine/screen_draw.h"
#include "engine/sky_state.h"
#include "engine/sun_occlusion.h"
#include "fcse_api.h"

#include "dazzle_accumulate_ps.h"
#include "dazzle_bleach_ps.h"
#include "dazzle_ps.h"

#include <cmath>
#include <cstdio>
#include <cstring>

namespace {
    // Where the engine binds these for every shader in the frame.
    constexpr uint32_t kViewProjectionRegister = 4;
    constexpr uint32_t kProjectionRegister = 8;
    constexpr uint32_t kCameraDirectionRegister = 46;

    // The sky pass is the only one of the frame's world passes drawn through a viewport squeezed
    // against the far plane, which is what tells it from the rest. It is also where the engine
    // draws the sun, so the world's depth is complete and still bound.
    constexpr float kSkyPassMinZ = 0.9f;

    constexpr float kPi = 3.14159265f;

    // How wide the glare's own gradient is allowed to get, whatever the spread is set to.
    constexpr float kMaxRadiusRadians = 70.0f * kPi / 180.0f;

    // How much longer the eye takes to recover than it spent being dazzled.
    constexpr float kLinger = 2.0f;

    // The sun overstimulates the red and green cones hardest, so the fatigued response first skews
    // towards their complement and then drifts as the cone types recover at different rates.
    constexpr float kCoreEarly[3] = {0.04f, 0.15f, 0.14f}; // dark desaturated cyan-green
    constexpr float kCoreLate[3] = {0.13f, 0.05f, 0.15f};  // dark magenta-purple

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

    // Where the bleached core begins and where it becomes solid within the mask, and the gap between
    // the two is its soft edge. Both sit low, because the mask is an average over the frames the eye
    // spent dazzled, and a view that wanders never holds any one part of it at full strength for
    // long.
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

    // Written by the settings callbacks and read while drawing. Each is a lone aligned float that
    // no other value has to agree with, so a torn read is neither possible nor consequential.
    float g_strength = 1.0f;
    float g_spreadRadians = 57.0f * kPi / 180.0f;
    float g_falloff = 2.0f;
    float g_contrast = 1.95f;
    float g_desaturation = 1.29f;
    float g_veil = 1.0f;
    float g_elevationRamp = 40.0f * kPi / 180.0f;
    float g_afterimageStrength = 1.0f;
    float g_afterimageSeconds = 10.0f;
    float g_afterimageDarkness = 0.9f;
    float g_afterimageTint = 0.35f;
    float g_afterimageHaze = 0.35f;
    float g_afterimageSize = 0.25f;
    float g_afterimageSaturation = 0.30f;

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

    // How long the eye has been dazzled and how much of that is left to recover. The afterimage
    // fades over twice as long as it took to build.
    bool g_dazzled = false;
    float g_exposure = 0.0f;
    float g_recovering = 0.0f;
    float g_burnWeight = 0.0f;
    float g_sinceLookAway = 0.0f;
    LARGE_INTEGER g_tickFrequency = {};
    LARGE_INTEGER g_lastTick = {};

    // Where the sun is relative to the view, measured at a scene pass and used at the composite.
    // Both run in that order on one thread, so this needs no synchronisation.
    struct SunInView {
        bool inFront;
        float x;
        float y;
        float cosAngle;
        float clipW;
        // One over the tangent of half the vertical field of view, which turns an angle into a
        // distance on screen.
        float verticalScale;
        float elevation;
        float storm;
    };

    SunInView g_thisFrame = {};

    // The same reckoning done at the composite, only to check that the sky pass has the world's
    // camera bound and not some other pass's.
    SunInView g_atComposite = {};

    // Where the sun lands on screen, from the transform bound right now.
    bool ComputeSun(IDirect3DDevice9* device, const D3DVIEWPORT9& viewport, SunInView& out) {
        out = SunInView{};

        SkyOverhaul::SkyState::Sun sun;
        if (!SkyOverhaul::SkyState::Latest(sun)) {
            return false;
        }

        float viewProjection[16] = {};
        float projection[16] = {};
        float camera[4] = {};
        if (FAILED(device->GetVertexShaderConstantF(kViewProjectionRegister, viewProjection, 4)) ||
            FAILED(device->GetVertexShaderConstantF(kProjectionRegister, projection, 4)) ||
            FAILED(device->GetVertexShaderConstantF(kCameraDirectionRegister, camera, 1))) {
            return false;
        }

        // The sun is a direction rather than a place, so it projects with w = 0: the point on the
        // far plane that direction points at, which is where the engine drew it.
        const float clipW = viewProjection[12] * sun.direction[0] +
                            viewProjection[13] * sun.direction[1] +
                            viewProjection[14] * sun.direction[2];
        const float clipX = viewProjection[0] * sun.direction[0] +
                            viewProjection[1] * sun.direction[1] +
                            viewProjection[2] * sun.direction[2];
        const float clipY = viewProjection[4] * sun.direction[0] +
                            viewProjection[5] * sun.direction[1] +
                            viewProjection[6] * sun.direction[2];

        const float cameraLength = std::sqrt(camera[0] * camera[0] + camera[1] * camera[1] +
                                             camera[2] * camera[2]);

        out.clipW = clipW;
        out.inFront = clipW > 0.0001f;
        out.verticalScale = projection[5];
        out.elevation = sun.direction[2];
        out.storm = sun.storm;
        if (cameraLength > 0.0001f) {
            out.cosAngle = (camera[0] * sun.direction[0] + camera[1] * sun.direction[1] +
                            camera[2] * sun.direction[2]) /
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

    // The extremes since the last line, so a two-second bucket still shows a moment of cover even
    // though only one line in it is written.
    float g_lowestVisible = 2.0f;
    float g_highestVisible = -1.0f;

    uint32_t g_lastCoverFrame = 0;

    // A copy of the finished frame to read while overwriting it, and the shader that does the
    // overwriting. Both belong to the device and are surrendered before it is reset.
    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DTexture9* g_sceneCopy = nullptr;
    IDirect3DSurface9* g_sceneCopySurface = nullptr;
    IDirect3DTexture9* g_burn = nullptr;
    IDirect3DSurface9* g_burnSurface = nullptr;
    IDirect3DTexture9* g_bleach = nullptr;
    IDirect3DSurface9* g_bleachSurface = nullptr;
    IDirect3DPixelShader9* g_shader = nullptr;
    IDirect3DPixelShader9* g_accumulateShader = nullptr;
    IDirect3DPixelShader9* g_bleachShader = nullptr;
    D3DSURFACE_DESC g_copyDesc = {};
    bool g_shaderRefused = false;

    // Nothing has been burned in yet, so there is nothing to show. Without this the first
    // afterimage would be whatever the texture's memory happened to hold.
    bool g_burnReady = false;

    // Set when the eye is dazzled afresh, so the burn starts from the current view rather than
    // fading up from what a previous dazzle left behind.
    bool g_burnRestart = false;

    HRESULT g_burnTargetResult = S_OK;
    uint32_t g_lastBurnLine = 0;

    void ReleaseCopy() {
        if (g_sceneCopySurface != nullptr) {
            g_sceneCopySurface->Release();
            g_sceneCopySurface = nullptr;
        }
        if (g_sceneCopy != nullptr) {
            g_sceneCopy->Release();
            g_sceneCopy = nullptr;
        }
        if (g_burnSurface != nullptr) {
            g_burnSurface->Release();
            g_burnSurface = nullptr;
        }
        if (g_burn != nullptr) {
            g_burn->Release();
            g_burn = nullptr;
        }
        if (g_bleachSurface != nullptr) {
            g_bleachSurface->Release();
            g_bleachSurface = nullptr;
        }
        if (g_bleach != nullptr) {
            g_bleach->Release();
            g_bleach = nullptr;
        }
        g_burnReady = false;
    }

    // Draws one accumulation pass into `into`. An add blends this frame in by the weight the shader
    // carries in its own alpha, averaging many frames; a maximum keeps whichever is brighter.
    void AccumulateInto(IDirect3DDevice9* device, IDirect3DSurface9* into,
                        IDirect3DPixelShader9* shader, const D3DSURFACE_DESC& backBuffer,
                        const float* constants, D3DBLENDOP blend) {
        IDirect3DSurface9* previous = nullptr;
        if (FAILED(device->GetRenderTarget(0, &previous)) || previous == nullptr) {
            return;
        }

        g_burnTargetResult = device->SetRenderTarget(0, into);
        if (SUCCEEDED(g_burnTargetResult)) {
            {
                SkyOverhaul::ScreenDraw draw(device);
                device->SetPixelShader(shader);
                device->SetTexture(0, g_sceneCopy);
                // A maximum takes the source whole, so it must not be weighted by the alpha the
                // running mean rides on.
                const bool peak = blend == D3DBLENDOP_MAX;
                device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
                device->SetRenderState(D3DRS_BLENDOP, blend);
                device->SetRenderState(D3DRS_SRCBLEND, peak ? D3DBLEND_ONE : D3DBLEND_SRCALPHA);
                device->SetRenderState(D3DRS_DESTBLEND,
                                       peak ? D3DBLEND_ONE : D3DBLEND_INVSRCALPHA);
                device->SetPixelShaderConstantF(0, constants, 6);

                draw.Quad(0.0f, 0.0f, static_cast<float>(backBuffer.Width),
                          static_cast<float>(backBuffer.Height));
            }
            device->SetRenderTarget(0, previous);
            g_burnReady = true;
        }

        previous->Release();
    }

    bool EnsureDeviceObjects(IDirect3DDevice9* device, const D3DSURFACE_DESC& backBuffer) {
        if (g_shaderRefused) {
            return false;
        }

        if (g_owner != device) {
            ReleaseCopy();
            if (g_shader != nullptr) {
                g_shader->Release();
                g_shader = nullptr;
            }
            if (g_accumulateShader != nullptr) {
                g_accumulateShader->Release();
                g_accumulateShader = nullptr;
            }
            if (g_bleachShader != nullptr) {
                g_bleachShader->Release();
                g_bleachShader = nullptr;
            }
            g_owner = device;
        }

        if (g_shader == nullptr || g_accumulateShader == nullptr || g_bleachShader == nullptr) {
            static_assert(sizeof(g_dazzlePixelShader) % sizeof(DWORD) == 0,
                          "the compiled shader is not a whole number of tokens");
            DWORD present[sizeof(g_dazzlePixelShader) / sizeof(DWORD)];
            DWORD accumulate[sizeof(g_dazzleAccumulatePixelShader) / sizeof(DWORD)];
            DWORD bleach[sizeof(g_dazzleBleachPixelShader) / sizeof(DWORD)];
            std::memcpy(present, g_dazzlePixelShader, sizeof(g_dazzlePixelShader));
            std::memcpy(accumulate, g_dazzleAccumulatePixelShader,
                        sizeof(g_dazzleAccumulatePixelShader));
            std::memcpy(bleach, g_dazzleBleachPixelShader, sizeof(g_dazzleBleachPixelShader));

            const HRESULT created = device->CreatePixelShader(present, &g_shader);
            const HRESULT createdAccumulate =
                device->CreatePixelShader(accumulate, &g_accumulateShader);
            const HRESULT createdBleach = device->CreatePixelShader(bleach, &g_bleachShader);
            if (FAILED(created) || FAILED(createdAccumulate) || FAILED(createdBleach) ||
                g_shader == nullptr || g_accumulateShader == nullptr || g_bleachShader == nullptr) {
                g_shaderRefused = true;
                char line[128];
                std::snprintf(line, sizeof(line),
                              "dazzle: CreatePixelShader failed 0x%08lX / 0x%08lX",
                              static_cast<unsigned long>(created),
                              static_cast<unsigned long>(createdAccumulate));
                FCSE::ApiPointer()->Log(line);
                return false;
            }
        }

        if (g_sceneCopy != nullptr && g_copyDesc.Width == backBuffer.Width &&
            g_copyDesc.Height == backBuffer.Height && g_copyDesc.Format == backBuffer.Format) {
            return true;
        }

        ReleaseCopy();
        const HRESULT created =
            device->CreateTexture(backBuffer.Width, backBuffer.Height, 1, D3DUSAGE_RENDERTARGET,
                                  backBuffer.Format, D3DPOOL_DEFAULT, &g_sceneCopy, nullptr);
        if (SUCCEEDED(created) &&
            SUCCEEDED(device->CreateTexture(backBuffer.Width, backBuffer.Height, 1,
                                            D3DUSAGE_RENDERTARGET, backBuffer.Format,
                                            D3DPOOL_DEFAULT, &g_burn, nullptr)) &&
            SUCCEEDED(device->CreateTexture(backBuffer.Width, backBuffer.Height, 1,
                                            D3DUSAGE_RENDERTARGET, backBuffer.Format,
                                            D3DPOOL_DEFAULT, &g_bleach, nullptr))) {
            g_burn->GetSurfaceLevel(0, &g_burnSurface);
            g_bleach->GetSurfaceLevel(0, &g_bleachSurface);
        }
        if (FAILED(created) || g_sceneCopy == nullptr || g_burnSurface == nullptr ||
            g_bleachSurface == nullptr ||
            FAILED(g_sceneCopy->GetSurfaceLevel(0, &g_sceneCopySurface))) {
            ReleaseCopy();
            char line[160];
            std::snprintf(line, sizeof(line), "dazzle: no %ux%u copy of the frame, 0x%08lX",
                          backBuffer.Width, backBuffer.Height,
                          static_cast<unsigned long>(created));
            FCSE::ApiPointer()->Log(line);
            return false;
        }

        g_copyDesc = backBuffer;

        char line[160];
        std::snprintf(line, sizeof(line), "dazzle: drawing over %ux%u, format %u",
                      backBuffer.Width, backBuffer.Height,
                      static_cast<unsigned>(backBuffer.Format));
        FCSE::ApiPointer()->Log(line);
        return true;
    }

    // Carries the eye's exposure forward by one frame and reports how strongly the afterimage
    // should show. It fades over twice as long as the eye spent dazzled, so a short stare leaves a
    // brief mark and a long one leaves a lasting one.
    float AdvanceAfterimage(const Glare& glare, bool live) {
        const float intensity = glare.intensity;

        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);

        float elapsed = 0.0f;
        if (g_lastTick.QuadPart != 0 && g_tickFrequency.QuadPart != 0) {
            elapsed = static_cast<float>(static_cast<double>(now.QuadPart - g_lastTick.QuadPart) /
                                         static_cast<double>(g_tickFrequency.QuadPart));
        }
        g_lastTick = now;

        if (!live || elapsed > kRecoveryGap) {
            g_dazzled = false;
            g_exposure = 0.0f;
            g_recovering = 0.0f;
            return 0.0f;
        }
        if (elapsed > kLongestFrame) {
            elapsed = kLongestFrame;
        }

        const bool dazzled = g_dazzled ? intensity > kDazzleOff : intensity > kDazzleOn;
        if (dazzled) {
            // A look back at the sun adds to what is already on the retina rather than wiping it.
            // Only an eye that has finished recovering starts a fresh image, and an eye that has
            // finished recovering is one whose exposure has already been cleared.
            if (!g_dazzled && g_exposure <= 0.0f) {
                g_sinceLookAway = 0.0f;
                g_burnRestart = true;
            }
            g_exposure += elapsed;
            if (g_exposure > g_afterimageSeconds) {
                g_exposure = g_afterimageSeconds;
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
        const float envelope = g_afterimageStrength * presence * pulse;

        g_recovery.core = envelope * g_afterimageDarkness;
        g_recovery.surround = envelope * g_afterimageTint;
        g_recovery.haze = g_afterimageStrength * presence * g_afterimageHaze;
        g_recovery.flash = phase < kFlashUntil
                               ? kFlashStrength * (1.0f - phase / kFlashUntil) * emerging * maturity
                               : 0.0f;
        for (int i = 0; i < 3; i++) {
            g_recovery.colour[i] = kCoreEarly[i] + (kCoreLate[i] - kCoreEarly[i]) * phase;
        }

        return envelope;
    }

    // How dazzled the eye is, from nothing to blinded, and how much of that light is drowning the
    // afterimage out.
    Glare Measure() {
        Glare glare = {};

        // Deliberately not gated on the sun being in front of the camera: the whole point is that
        // the angle decides, and a wide spread reaches past a right angle.
        if (g_strength <= 0.0f) {
            return glare;
        }

        const float cosAngle = g_thisFrame.cosAngle < -1.0f
                                   ? -1.0f
                                   : (g_thisFrame.cosAngle > 1.0f ? 1.0f : g_thisFrame.cosAngle);
        const float angle = std::acos(cosAngle);
        if (angle >= g_spreadRadians) {
            return glare;
        }

        // Raised to a power rather than eased symmetrically: full strength has to mean the sun in
        // the middle of the view, not merely somewhere on screen, so the curve has to fall away
        // from its peak immediately.
        const float t = angle / g_spreadRadians;
        const float proximity = std::pow(1.0f - t, g_falloff);

        // A low sun is a weak one: its light travels further through the atmosphere.
        const float ramp = std::sin(g_elevationRamp);
        float elevation = ramp > 0.0001f ? g_thisFrame.elevation / ramp : 1.0f;
        elevation = elevation < 0.0f ? 0.0f : (elevation > 1.0f ? 1.0f : elevation);

        const float measured = SkyOverhaul::SunOcclusion::Visibility();
        const float visible = measured < 0.0f ? 1.0f : (measured > 1.0f ? 1.0f : measured);

        const float storm = g_thisFrame.storm > 1.0f ? 1.0f : g_thisFrame.storm;

        glare.intensity = g_strength * proximity * elevation * visible * (1.0f - storm);

        // Light hides the afterimage over a far wider cone than it brightens the frame over, and
        // it hides it completely wherever the sun is anywhere near the middle of the view - a low
        // sun blinds the eye to its own afterimage just as well as a high one, which is why the
        // elevation has no part in this. Only cover releases it early.
        glare.hold = (1.0f - t * t) * visible * (1.0f - storm);

        return glare;
    }

    // Takes a copy of the finished frame and paints it back through the shader. The copy is needed
    // because the shader reads the same pixels it writes, which no blend state can express.
    void DrawDazzle(const SkyOverhaul::Frame::Pass& pass, float intensity, float afterimage) {
        if (!EnsureDeviceObjects(pass.device, pass.backBuffer)) {
            return;
        }

        IDirect3DSurface9* backBuffer = nullptr;
        if (FAILED(pass.device->GetRenderTarget(0, &backBuffer)) || backBuffer == nullptr) {
            return;
        }
        const HRESULT copied =
            pass.device->StretchRect(backBuffer, nullptr, g_sceneCopySurface, nullptr,
                                     D3DTEXF_NONE);

        backBuffer->Release();
        if (FAILED(copied)) {
            return;
        }

        const float width = static_cast<float>(pass.backBuffer.Width);
        const float height = static_cast<float>(pass.backBuffer.Height);

        // The vertical half of the frame spans the tangent of half the field of view, so the
        // spread converts into the same units the texture coordinates are measured in. Capped well
        // short of a right angle, where the tangent runs away and then turns negative - the veil,
        // not this gradient, is what carries a spread wider than the screen.
        const float radiusAngle =
            g_spreadRadians > kMaxRadiusRadians ? kMaxRadiusRadians : g_spreadRadians;
        const float radius = 0.5f * std::tan(radiusAngle) * g_thisFrame.verticalScale;

        const bool showing = g_burnReady && afterimage > 0.0f;
        const float constants[24] = {
            g_thisFrame.x / width,
            g_thisFrame.y / height,
            intensity,
            g_contrast,

            radius,
            width / height,
            g_veil,
            g_desaturation,

            showing ? g_recovery.core : 0.0f,
            g_burnRestart || !g_burnReady ? 1.0f : g_burnWeight,
            showing ? g_recovery.surround : 0.0f,
            showing ? g_recovery.flash : 0.0f,

            g_recovery.colour[0],
            g_recovery.colour[1],
            g_recovery.colour[2],
            showing ? g_recovery.haze : 0.0f,

            kCoreLow,
            kCoreHigh,
            g_afterimageSaturation,
            0.0f,

            radius * g_afterimageSize,
            0.0f,
            0.0f,
            0.0f};

        // Built up while the eye is dazzled and left alone afterwards, so what fades is what the
        // eye was looking at. Taken from the copy made before the glare is painted on, which is
        // what puts a dark spot where the sun was rather than a white one.
        if (g_dazzled) {
            const bool restart = g_burnRestart || !g_burnReady;
            g_burnRestart = false;
            AccumulateInto(pass.device, g_burnSurface, g_accumulateShader, pass.backBuffer,
                           constants, D3DBLENDOP_ADD);

            // The bleach keeps the most light that fell on each part of the view rather than the
            // average of it: a burned retina does not un-burn when the sun moves on. It also has
            // to. At several hundred frames a second the mean's weight is a thousandth, which
            // cannot move an eight-bit target by a single step, so the mask would sit frozen on
            // the first frame of the stare - the weakest one there is.
            AccumulateInto(pass.device, g_bleachSurface, g_bleachShader, pass.backBuffer, constants,
                           restart ? D3DBLENDOP_ADD : D3DBLENDOP_MAX);
        }

        SkyOverhaul::ScreenDraw draw(pass.device);
        pass.device->SetPixelShader(g_shader);
        pass.device->SetTexture(0, g_sceneCopy);
        pass.device->SetTexture(1, g_burn);
        pass.device->SetTexture(2, g_bleach);
        pass.device->SetPixelShaderConstantF(0, constants, 6);

        draw.Quad(0.0f, 0.0f, width, height);
    }

    void OnScenePass(const SkyOverhaul::Frame::Pass& pass) {
        // Only the sky pass is worth reading: it is the one the engine draws the sun in, so the
        // world's depth is complete and still bound, and the transform is the one it drew with.
        if (pass.viewport.MinZ < kSkyPassMinZ) {
            return;
        }
        if (!ComputeSun(pass.device, pass.viewport, g_thisFrame) || !g_thisFrame.inFront ||
            !pass.live) {
            return;
        }

        // Measured here rather than at the composite because a depth surface's contents are not
        // guaranteed across being unbound, and the bloom chain between the two unbinds it.
        SkyOverhaul::SunOcclusion::Sample(pass.device, g_thisFrame.x, g_thisFrame.y, pass.viewport);
    }

    void OnFinalPass(const SkyOverhaul::Frame::Pass& pass) {
        ComputeSun(pass.device, pass.viewport, g_atComposite);

        // The composite is the last moment the world owns the frame: the interface is drawn after
        // it, so the glare lands under the heads-up display rather than over it.
        const Glare glare = pass.live ? Measure() : Glare{};
        const float intensity = glare.intensity;
        const float afterimage = AdvanceAfterimage(glare, pass.live);
        if (intensity > 0.002f || afterimage > 0.002f) {
            DrawDazzle(pass, intensity, afterimage);
        }

        const float visible = SkyOverhaul::SunOcclusion::Visibility();
        if (visible >= 0.0f) {
            g_lowestVisible = visible < g_lowestVisible ? visible : g_lowestVisible;
            g_highestVisible = visible > g_highestVisible ? visible : g_highestVisible;
        }

        if (pass.live && (g_dazzled || afterimage > 0.0f) && pass.frame - g_lastBurnLine >= 30) {
            g_lastBurnLine = pass.frame;
            char line[256];
            std::snprintf(line, sizeof(line),
                          "burn f%u: dazzled %d ready %d weight %.3f exposure %.2f recovering %.2f "
                          "hold %.3f env %.3f core %.3f haze %.3f target 0x%08lX",
                          pass.frame, g_dazzled ? 1 : 0, g_burnReady ? 1 : 0, g_burnWeight,
                          g_exposure, g_recovering, glare.hold, afterimage, g_recovery.core,
                          g_recovery.haze, static_cast<unsigned long>(g_burnTargetResult));
            FCSE::ApiPointer()->Log(line);
        }

        // Anything short of fully visible gets a line of its own. Whether cover registers at all
        // is the open question, and a two-second bucket can pass straight over the moment it does.
        if (pass.live && visible >= 0.0f && visible < 0.9f && pass.frame - g_lastCoverFrame >= 10) {
            g_lastCoverFrame = pass.frame;
            unsigned long reached = 0;
            unsigned long total = 0;
            SkyOverhaul::SunOcclusion::LastCounts(reached, total);

            char line[192];
            std::snprintf(line, sizeof(line),
                          "cover f%u: visible %.3f query %lu/%lu sun (%.0f %.0f) cos %.3f",
                          pass.frame, visible, reached, total, g_thisFrame.x, g_thisFrame.y,
                          g_thisFrame.cosAngle);
            FCSE::ApiPointer()->Log(line);
        }

        // Two-second buckets, because what matters is how the measurement moves as the player
        // walks into cover, and that happens whenever the player gets there.
        if (pass.live && pass.frame % 120 == 0) {
            unsigned long reached = 0;
            unsigned long total = 0;
            SkyOverhaul::SunOcclusion::LastCounts(reached, total);

            char line[256];
            std::snprintf(line, sizeof(line),
                          "dazzle f%u: intensity %.3f | cos %.3f elev %.3f storm %.2f "
                          "visible %.3f (low %.3f high %.3f) | sun (%.0f %.0f) inFront %d",
                          pass.frame, intensity, g_thisFrame.cosAngle, g_thisFrame.elevation,
                          g_thisFrame.storm, visible, g_lowestVisible, g_highestVisible,
                          g_thisFrame.x, g_thisFrame.y, g_thisFrame.inFront ? 1 : 0);
            FCSE::ApiPointer()->Log(line);

            g_lowestVisible = 2.0f;
            g_highestVisible = -1.0f;
        }
    }
}

bool SkyOverhaul::Dazzle::Install() {
    QueryPerformanceFrequency(&g_tickFrequency);
    return SkyOverhaul::Frame::Install(&OnScenePass, &OnFinalPass);
}

void SkyOverhaul::Dazzle::SetStrength(int percent) {
    g_strength = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::SetSpread(int degrees) {
    g_spreadRadians = static_cast<float>(degrees) * kPi / 180.0f;
}

void SkyOverhaul::Dazzle::SetFalloff(int tenths) {
    g_falloff = static_cast<float>(tenths) / 10.0f;
}

void SkyOverhaul::Dazzle::SetContrast(int percent) {
    g_contrast = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::SetDesaturation(int percent) {
    g_desaturation = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::SetVeil(int percent) {
    g_veil = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::SetElevationRamp(int degrees) {
    g_elevationRamp = static_cast<float>(degrees) * kPi / 180.0f;
}

void SkyOverhaul::Dazzle::SetAfterimageStrength(int percent) {
    g_afterimageStrength = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::SetAfterimageSeconds(int seconds) {
    g_afterimageSeconds = static_cast<float>(seconds);
}

void SkyOverhaul::Dazzle::SetAfterimageDarkness(int percent) {
    g_afterimageDarkness = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::SetAfterimageTint(int percent) {
    g_afterimageTint = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::SetAfterimageHaze(int percent) {
    g_afterimageHaze = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::SetAfterimageSize(int percent) {
    g_afterimageSize = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::SetAfterimageSaturation(int percent) {
    g_afterimageSaturation = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Dazzle::ReleaseDeviceObjects() {
    ReleaseCopy();
    if (g_shader != nullptr) {
        g_shader->Release();
        g_shader = nullptr;
    }
    if (g_accumulateShader != nullptr) {
        g_accumulateShader->Release();
        g_accumulateShader = nullptr;
    }
    if (g_bleachShader != nullptr) {
        g_bleachShader->Release();
        g_bleachShader = nullptr;
    }
    g_owner = nullptr;
    SkyOverhaul::SunOcclusion::ReleaseDeviceObjects();
}
