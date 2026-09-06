// The glare is computed here rather than in a shader because no shader in the game is given both
// halves of the angle it depends on: the camera is a global, the sun is not.
#include "dazzle.h"

#include "engine/frame.h"
#include "engine/screen_draw.h"
#include "engine/sky_state.h"
#include "engine/sun_occlusion.h"
#include "fcse_api.h"

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

    // Written by the settings callbacks and read while drawing. Each is a lone aligned float that
    // no other value has to agree with, so a torn read is neither possible nor consequential.
    float g_strength = 1.0f;
    float g_spreadRadians = 57.0f * kPi / 180.0f;
    float g_falloff = 2.0f;
    float g_contrast = 1.95f;
    float g_desaturation = 1.29f;
    float g_veil = 1.0f;
    float g_elevationRamp = 40.0f * kPi / 180.0f;

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
    IDirect3DPixelShader9* g_shader = nullptr;
    D3DSURFACE_DESC g_copyDesc = {};
    bool g_shaderRefused = false;

    void ReleaseCopy() {
        if (g_sceneCopySurface != nullptr) {
            g_sceneCopySurface->Release();
            g_sceneCopySurface = nullptr;
        }
        if (g_sceneCopy != nullptr) {
            g_sceneCopy->Release();
            g_sceneCopy = nullptr;
        }
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
            g_owner = device;
        }

        if (g_shader == nullptr) {
            static_assert(sizeof(g_dazzlePixelShader) % sizeof(DWORD) == 0,
                          "the compiled shader is not a whole number of tokens");
            DWORD tokens[sizeof(g_dazzlePixelShader) / sizeof(DWORD)];
            std::memcpy(tokens, g_dazzlePixelShader, sizeof(g_dazzlePixelShader));

            const HRESULT created = device->CreatePixelShader(tokens, &g_shader);
            if (FAILED(created) || g_shader == nullptr) {
                g_shaderRefused = true;
                char line[128];
                std::snprintf(line, sizeof(line), "dazzle: CreatePixelShader failed 0x%08lX",
                              static_cast<unsigned long>(created));
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
        if (FAILED(created) || g_sceneCopy == nullptr ||
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

    // How dazzled the eye is, from nothing to blinded.
    float Intensity() {
        // Deliberately not gated on the sun being in front of the camera: the whole point is that
        // the angle decides, and a wide spread reaches past a right angle.
        if (g_strength <= 0.0f) {
            return 0.0f;
        }

        const float cosAngle = g_thisFrame.cosAngle < -1.0f
                                   ? -1.0f
                                   : (g_thisFrame.cosAngle > 1.0f ? 1.0f : g_thisFrame.cosAngle);
        const float angle = std::acos(cosAngle);
        if (angle >= g_spreadRadians) {
            return 0.0f;
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
        const float visible = measured < 0.0f ? 1.0f : measured;

        const float storm = g_thisFrame.storm > 1.0f ? 1.0f : g_thisFrame.storm;

        return g_strength * proximity * elevation * visible * (1.0f - storm);
    }

    // Takes a copy of the finished frame and paints it back through the shader. The copy is needed
    // because the shader reads the same pixels it writes, which no blend state can express.
    void DrawDazzle(const SkyOverhaul::Frame::Pass& pass, float intensity) {
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

        SkyOverhaul::ScreenDraw draw(pass.device);
        pass.device->SetPixelShader(g_shader);
        pass.device->SetTexture(0, g_sceneCopy);

        const float width = static_cast<float>(pass.backBuffer.Width);
        const float height = static_cast<float>(pass.backBuffer.Height);

        // The vertical half of the frame spans the tangent of half the field of view, so the
        // spread converts into the same units the texture coordinates are measured in. Capped well
        // short of a right angle, where the tangent runs away and then turns negative - the veil,
        // not this gradient, is what carries a spread wider than the screen.
        const float radiusAngle =
            g_spreadRadians > kMaxRadiusRadians ? kMaxRadiusRadians : g_spreadRadians;
        const float radius = 0.5f * std::tan(radiusAngle) * g_thisFrame.verticalScale;

        const float constants[8] = {
            g_thisFrame.x / width, g_thisFrame.y / height, intensity, g_contrast,
            radius,                width / height,         g_veil,    g_desaturation};
        pass.device->SetPixelShaderConstantF(0, constants, 2);

        draw.Quad(0.0f, 0.0f, static_cast<float>(pass.backBuffer.Width),
                  static_cast<float>(pass.backBuffer.Height));
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
        const float intensity = pass.live ? Intensity() : 0.0f;
        if (intensity > 0.002f) {
            DrawDazzle(pass, intensity);
        }

        const float visible = SkyOverhaul::SunOcclusion::Visibility();
        if (visible >= 0.0f) {
            g_lowestVisible = visible < g_lowestVisible ? visible : g_lowestVisible;
            g_highestVisible = visible > g_highestVisible ? visible : g_highestVisible;
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

void SkyOverhaul::Dazzle::ReleaseDeviceObjects() {
    ReleaseCopy();
    if (g_shader != nullptr) {
        g_shader->Release();
        g_shader = nullptr;
    }
    g_owner = nullptr;
    SkyOverhaul::SunOcclusion::ReleaseDeviceObjects();
}
