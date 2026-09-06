// A veiling-glare wash over the finished frame, centred on the sun.
//
// It is drawn here rather than in a shader because no shader can be given both halves of the angle
// it depends on. The camera's direction and the view-projection matrix are globals the engine binds
// to every shader; the sun's direction is not bound at all, since the only draws that need it are
// already placed at the sun. Every attempt to do this inside the sun's own flare shader ran into
// the same wall: a pixel there knows its own distance from the sun but not the sun's distance from
// the centre of the screen.
//
// The wash is a gradient, not a flat lift. Glare is light scattered off the sun inside the eye, so
// it is brightest beside the sun and falls away from it - a uniform brightening reads as fog over
// the whole picture instead. The sun is projected to its place on screen and the gradient is drawn
// as a fan around that point, so it lands on the sun the engine already drew and falls off across
// the frame.
//
// Two things it must not do, both of which the engine already knows the answer to:
//
//   - Glare through cover. Measured during the sky pass, where the scene depth still exists, by
//     drawing the sun's disc into an occlusion query. See engine/sun_occlusion.cpp.
//   - Glare in a menu. Nothing is asked about game state; the sun-disc hook reports whether the
//     engine drew a sky this frame, and a frame without one gets nothing.
//
// EndScene rather than Present, because Present is the obvious seam and so already taken - DevTools
// draws its overlay from there, and FCSE gives an address to one plugin only. EndScene runs with
// the scene still open and the frame otherwise finished, which suits this better anyway: no scene
// of our own to start, and no Reset hook to own, since nothing here outlives a single call.
#include "veil.h"

#include "engine/sky_state.h"
#include "engine/sun_occlusion.h"
#include "fcse_api.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <d3d9.h>
#include <windows.h>

namespace {
    constexpr size_t kEndSceneSlot = 42;

    // Where the engine binds these for every shader in the frame, from the shipped shader sources'
    // global parameter block.
    constexpr uint32_t kViewProjectionRegister = 4;  // _ViewProjectionMatrix, four registers
    constexpr uint32_t kProjectionRegister = 8;      // _ProjectionMatrix, four registers
    constexpr uint32_t kCameraDirectionRegister = 46;


    constexpr float kPi = 3.14159265f;

    // Segments around the gradient's rim. Enough that its edge reads as a circle rather than a
    // polygon at the radii this draws at.
    constexpr int kSegments = 64;

    using EndSceneFn = HRESULT(__stdcall*)(IDirect3DDevice9*);

    EndSceneFn g_originalEndScene = nullptr;
    bool g_inEndScene = false;

    float g_strength = 1.0f;
    float g_spreadRadians = 55.0f * kPi / 180.0f;

    // Two budgets, because they compete: menu frames refuse over and over, and if they shared a
    // budget with the real thing they would spend it before the player ever loaded a world - which
    // is exactly what happened the first time.
    int g_framesLogged = 0;
    int g_framesSeen = 0;
    int g_refusalsLogged = 0;

    struct ScreenVertex {
        float x, y, z, rhw;
        D3DCOLOR colour;
    };

    constexpr DWORD kScreenVertexFormat = D3DFVF_XYZRHW | D3DFVF_DIFFUSE;

    constexpr D3DRENDERSTATETYPE kRenderStates[] = {
        D3DRS_ZENABLE,          D3DRS_ZWRITEENABLE,     D3DRS_ZFUNC,
        D3DRS_CULLMODE,         D3DRS_LIGHTING,         D3DRS_FOGENABLE,
        D3DRS_STENCILENABLE,    D3DRS_SCISSORTESTENABLE, D3DRS_COLORWRITEENABLE,
        D3DRS_ALPHATESTENABLE,  D3DRS_ALPHABLENDENABLE, D3DRS_SRCBLEND,
        D3DRS_DESTBLEND,        D3DRS_BLENDOP,          D3DRS_SEPARATEALPHABLENDENABLE,
        D3DRS_SHADEMODE,
    };
    constexpr D3DTEXTURESTAGESTATETYPE kStageStates[] = {
        D3DTSS_COLOROP, D3DTSS_COLORARG1, D3DTSS_ALPHAOP, D3DTSS_ALPHAARG1,
    };

    // What the glare looks like this frame: how bright at the sun, and where the sun is.
    struct Glare {
        float peak;      // 0 when there is nothing to draw
        float centreX;   // pixels
        float centreY;
        float radius;    // pixels, where the gradient reaches nothing
    };

    Glare Nothing() { return Glare{0.0f, 0.0f, 0.0f, 0.0f}; }

    // Says why a frame drew nothing, for the first few frames, so a glare that never appears names
    // its own reason in fcse.log instead of leaving the whole chain to be guessed at.
    Glare Refuse(const char* reason) {
        if (g_refusalsLogged < 6) {
            g_refusalsLogged++;
            char line[128];
            std::snprintf(line, sizeof(line), "veil: nothing drawn - %s", reason);
            FCSE::ApiPointer()->Log(line);
        }
        return Nothing();
    }

    Glare ComputeGlare(IDirect3DDevice9* device, const D3DVIEWPORT9& viewport) {
        float sun[3];
        if (g_strength <= 0.0f) {
            return Refuse("strength is zero");
        }
        if (!SkyOverhaul::SkyState::SunDirection(sun)) {
            return Refuse("no sun direction yet");
        }

        float viewProjection[16] = {};
        float projection[16] = {};
        float camera[4] = {};
        if (FAILED(device->GetVertexShaderConstantF(kViewProjectionRegister, viewProjection, 4)) ||
            FAILED(device->GetVertexShaderConstantF(kProjectionRegister, projection, 4)) ||
            FAILED(device->GetVertexShaderConstantF(kCameraDirectionRegister, camera, 1))) {
            return Refuse("the shader constants could not be read back");
        }

        // Measured during the sky pass rather than read from a constant. The one global that sounds
        // like this, SunOcclusionFactor at register 63, is used by a single shipped shader to scale
        // water specular and sits at 1.0 throughout play, so it never described cover at all.
        const float sampled = SkyOverhaul::SkyState::SunVisibility();

        // The sun is a direction rather than a place, so it projects with w = 0: the point on the
        // far plane that direction points at, which is where the engine drew it.
        const float clipW = viewProjection[12] * sun[0] + viewProjection[13] * sun[1] +
                            viewProjection[14] * sun[2];
        if (clipW <= 0.0001f) {
            return Refuse("the sun is behind the camera");
        }
        const float clipX = viewProjection[0] * sun[0] + viewProjection[1] * sun[1] +
                            viewProjection[2] * sun[2];
        const float clipY = viewProjection[4] * sun[0] + viewProjection[5] * sun[1] +
                            viewProjection[6] * sun[2];

        // Negative until the first measurement comes back, which takes a couple of frames because
        // the query is read without stalling; treat that as unoccluded rather than as darkness.
        // Zero is not turned into an early exit, so that a frame the sun is hidden in still reaches
        // the log below - the case worth seeing is exactly the one that draws nothing.
        const float visible = sampled < 0.0f ? 1.0f : sampled;

        // Below the horizon there is no sun to be dazzled by, faded over the last few degrees so
        // the glare does not switch off at sunset.
        const float aboveHorizon = sun[2] / 0.08f;
        if (aboveHorizon <= 0.0f) {
            return Refuse("the sun is below the horizon");
        }

        const float cameraLength = std::sqrt(camera[0] * camera[0] + camera[1] * camera[1] +
                                             camera[2] * camera[2]);
        if (cameraLength < 0.0001f) {
            return Refuse("nothing has bound the camera direction");
        }

        // The projection's vertical term is one over the tangent of half the field of view, which
        // is what turns the spread from an angle into a distance on screen.
        const float pixelsPerTangent = static_cast<float>(viewport.Height) * 0.5f * projection[5];

        Glare glare;
        glare.centreX = static_cast<float>(viewport.X) +
                        (clipX / clipW * 0.5f + 0.5f) * static_cast<float>(viewport.Width);
        glare.centreY = static_cast<float>(viewport.Y) +
                        (0.5f - clipY / clipW * 0.5f) * static_cast<float>(viewport.Height);
        glare.radius = std::tan(g_spreadRadians) * pixelsPerTangent;
        glare.peak = g_strength * visible * (aboveHorizon < 1.0f ? aboveHorizon : 1.0f);

        // Every second or so rather than only at the start, because what matters is how these move
        // as the player walks into cover, and the first few frames of a level never show that.
        if (++g_framesSeen % 60 == 0 && g_framesLogged < 40) {
            g_framesLogged++;
            unsigned long reached = 0;
            unsigned long total = 0;
            SkyOverhaul::SunOcclusion::LastCounts(reached, total);

            char line[256];
            const float towardsSun = (camera[0] * sun[0] + camera[1] * sun[1] + camera[2] * sun[2]) /
                                     cameraLength;
            std::snprintf(line, sizeof(line),
                          "veil: cos %.3f query %lu/%lu visible %.3f at (%.0f %.0f) radius %.0f "
                          "peak %.3f",
                          towardsSun, reached, total, visible, glare.centreX, glare.centreY,
                          glare.radius, glare.peak);
            FCSE::ApiPointer()->Log(line);
        }

        return glare;
    }

    void DrawGlare(IDirect3DDevice9* device, const Glare& glare) {
        DWORD savedRenderState[sizeof(kRenderStates) / sizeof(kRenderStates[0])] = {};
        for (size_t i = 0; i < sizeof(kRenderStates) / sizeof(kRenderStates[0]); i++) {
            device->GetRenderState(kRenderStates[i], &savedRenderState[i]);
        }
        DWORD savedStageState[sizeof(kStageStates) / sizeof(kStageStates[0])] = {};
        for (size_t i = 0; i < sizeof(kStageStates) / sizeof(kStageStates[0]); i++) {
            device->GetTextureStageState(0, kStageStates[i], &savedStageState[i]);
        }

        IDirect3DVertexShader9* savedVertexShader = nullptr;
        IDirect3DPixelShader9* savedPixelShader = nullptr;
        IDirect3DBaseTexture9* savedTexture = nullptr;
        DWORD savedFvf = 0;
        device->GetVertexShader(&savedVertexShader);
        device->GetPixelShader(&savedPixelShader);
        device->GetTexture(0, &savedTexture);
        device->GetFVF(&savedFvf);

        const BYTE level = static_cast<BYTE>(glare.peak * 255.0f + 0.5f);
        const D3DCOLOR centre = D3DCOLOR_ARGB(255, level, level, level);
        const D3DCOLOR rim = D3DCOLOR_ARGB(255, 0, 0, 0);

        // Just short of the far plane, so a depth test rejects it behind anything the engine drew
        // but still lets it through where only sky was written - the sky leaves depth at its
        // cleared value, because every sky pass draws with depth writes off.
        // Drawn without a depth test: the scene depth is already gone by this point in the frame,
        // which is what the occlusion measurement taken during the sky pass exists to replace.
        const float depth = 0.0f;

        // A fan: bright at the sun, nothing at the rim, and Gouraud between. The gradient is the
        // whole point, so it is carried by the vertices rather than by a texture nothing ships.
        ScreenVertex fan[kSegments + 2];
        fan[0] = {glare.centreX, glare.centreY, depth, 1.0f, centre};
        for (int i = 0; i <= kSegments; i++) {
            const float angle = static_cast<float>(i) * (2.0f * kPi / kSegments);
            fan[i + 1] = {glare.centreX + std::cos(angle) * glare.radius,
                          glare.centreY + std::sin(angle) * glare.radius, depth, 1.0f, rim};
        }

        device->SetVertexShader(nullptr);
        device->SetPixelShader(nullptr);
        device->SetTexture(0, nullptr);
        device->SetFVF(kScreenVertexFormat);

        device->SetRenderState(D3DRS_ZENABLE, FALSE);
        device->SetRenderState(D3DRS_ZFUNC, D3DCMP_LESSEQUAL);
        device->SetRenderState(D3DRS_ZWRITEENABLE, FALSE);
        device->SetRenderState(D3DRS_CULLMODE, D3DCULL_NONE);
        device->SetRenderState(D3DRS_LIGHTING, FALSE);
        device->SetRenderState(D3DRS_FOGENABLE, FALSE);
        device->SetRenderState(D3DRS_STENCILENABLE, FALSE);
        device->SetRenderState(D3DRS_SCISSORTESTENABLE, FALSE);
        device->SetRenderState(D3DRS_SHADEMODE, D3DSHADE_GOURAUD);
        device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                           D3DCOLORWRITEENABLE_GREEN |
                                                           D3DCOLORWRITEENABLE_BLUE);
        device->SetRenderState(D3DRS_ALPHATESTENABLE, FALSE);
        device->SetRenderState(D3DRS_SEPARATEALPHABLENDENABLE, FALSE);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
        device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_ONE);
        device->SetRenderState(D3DRS_BLENDOP, D3DBLENDOP_ADD);

        device->SetTextureStageState(0, D3DTSS_COLOROP, D3DTOP_SELECTARG1);
        device->SetTextureStageState(0, D3DTSS_COLORARG1, D3DTA_DIFFUSE);
        device->SetTextureStageState(0, D3DTSS_ALPHAOP, D3DTOP_SELECTARG1);
        device->SetTextureStageState(0, D3DTSS_ALPHAARG1, D3DTA_DIFFUSE);

        device->DrawPrimitiveUP(D3DPT_TRIANGLEFAN, kSegments, fan, sizeof(ScreenVertex));

        for (size_t i = 0; i < sizeof(kRenderStates) / sizeof(kRenderStates[0]); i++) {
            device->SetRenderState(kRenderStates[i], savedRenderState[i]);
        }
        for (size_t i = 0; i < sizeof(kStageStates) / sizeof(kStageStates[0]); i++) {
            device->SetTextureStageState(0, kStageStates[i], savedStageState[i]);
        }
        device->SetVertexShader(savedVertexShader);
        device->SetPixelShader(savedPixelShader);
        device->SetTexture(0, savedTexture);
        device->SetFVF(savedFvf);

        if (savedVertexShader != nullptr) {
            savedVertexShader->Release();
        }
        if (savedPixelShader != nullptr) {
            savedPixelShader->Release();
        }
        if (savedTexture != nullptr) {
            savedTexture->Release();
        }
    }

    // Whether this is the scene that ends up on screen. The engine closes several per frame for
    // its own render targets, and the glare belongs on the one drawing into the back buffer.
    bool TargetsBackBuffer(IDirect3DDevice9* device) {
        IDirect3DSurface9* target = nullptr;
        if (FAILED(device->GetRenderTarget(0, &target)) || target == nullptr) {
            return false;
        }

        IDirect3DSurface9* backBuffer = nullptr;
        const bool haveBackBuffer =
            SUCCEEDED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backBuffer)) &&
            backBuffer != nullptr;
        const bool matches = haveBackBuffer && target == backBuffer;

        if (haveBackBuffer) {
            backBuffer->Release();
        }
        target->Release();
        return matches;
    }

    HRESULT __stdcall EndSceneDetour(IDirect3DDevice9* device) {
        // Handed over every frame so the sky draw, which runs long before this, has something to
        // sample the sun's occlusion through. It is the same device for the life of the process.
        SkyOverhaul::SkyState::SetDevice(device);

        if (!g_inEndScene) {
            g_inEndScene = true;

            // The back-buffer test comes first because the flag below clears itself when read. The
            // engine closes several scenes per frame for its own render targets, and asking on each
            // of them meant an earlier one ate the answer before the scene that reaches the screen
            // could use it - which is why this drew nothing at all.
            D3DVIEWPORT9 viewport;
            if (TargetsBackBuffer(device) && SUCCEEDED(device->GetViewport(&viewport))) {
                if (!SkyOverhaul::SkyState::ConsumeSunDrawn()) {
                    Refuse("no sky was drawn this frame"); // a menu, a loading screen, a cutscene
                } else {
                    const Glare glare = ComputeGlare(device, viewport);
                    if (glare.peak > 0.0f && glare.radius > 1.0f) {
                        DrawGlare(device, glare);
                    }
                }
            }
            g_inEndScene = false;
        }
        return g_originalEndScene(device);
    }

    // Every IDirect3DDevice9 in a process shares one vtable, so a device of our own is enough to
    // name the function the game's device will call. The slot is read while that device is still
    // alive: a wrapper can put its vtable in the object's own allocation, and reading it after the
    // release is a use-after-free - which is how the first version of this crashed.
    void* ReadEndSceneSlot() {
        IDirect3D9* d3d = Direct3DCreate9(D3D_SDK_VERSION);
        if (d3d == nullptr) {
            return nullptr;
        }

        D3DPRESENT_PARAMETERS present = {};
        present.Windowed = TRUE;
        present.SwapEffect = D3DSWAPEFFECT_DISCARD;
        present.BackBufferFormat = D3DFMT_UNKNOWN;
        present.hDeviceWindow = GetDesktopWindow();

        IDirect3DDevice9* device = nullptr;
        void* endScene = nullptr;
        if (SUCCEEDED(d3d->CreateDevice(D3DADAPTER_DEFAULT, D3DDEVTYPE_HAL, present.hDeviceWindow,
                                        D3DCREATE_SOFTWARE_VERTEXPROCESSING, &present, &device)) &&
            device != nullptr) {
            endScene = (*reinterpret_cast<void***>(device))[kEndSceneSlot];
            device->Release();
        }
        d3d->Release();
        return endScene;
    }
}

bool SkyOverhaul::Veil::Install() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    void* endScene = ReadEndSceneSlot();
    if (endScene == nullptr) {
        api->Log("veil: no Direct3D 9 device could be created to read the vtable from");
        return false;
    }

    // A rejected hook is already logged by FCSE, naming the plugin that owns the address.
    if (!api->Hook(endScene, reinterpret_cast<void*>(&EndSceneDetour),
                   reinterpret_cast<void**>(&g_originalEndScene))) {
        return false;
    }

    char line[128];
    std::snprintf(line, sizeof(line), "veil: drawing from EndScene at 0x%08zX",
                  reinterpret_cast<size_t>(endScene));
    api->Log(line);
    return true;
}

void SkyOverhaul::Veil::SetStrength(int percent) {
    g_strength = static_cast<float>(percent) / 100.0f;
}

void SkyOverhaul::Veil::SetSpread(int degrees) {
    const float clamped = degrees < 1 ? 1.0f : (degrees > 85 ? 85.0f : static_cast<float>(degrees));
    g_spreadRadians = clamped * kPi / 180.0f;
}
