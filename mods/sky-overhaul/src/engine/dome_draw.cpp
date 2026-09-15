#include "engine/dome_draw.h"

#include "engine/depth_texture.h"
#include "engine/frame.h"
#include "engine/known_shaders.h"
#include "engine/solid_depth.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <windows.h>

namespace {
    constexpr size_t kDrawIndexedPrimitiveSlot = 82;

    // What the sky dome is, measured over sixty-one sky passes across noon, dusk, night, the pause
    // menu and a water reflection: one indexed triangle strip of 714 vertices and 1450 triangles,
    // drawn exactly once in every sky pass, at every hour. Nothing else drawn at the far plane
    // shares those two counts - the cloud bowl is 612 and 1171, the celestial sprites are 6 and 4 -
    // so they are the whole test, and both are arguments the caller already passed.
    //
    // The dome is the only draw binding the 64 by 512 colour ramp atlas, which is what proved it is
    // the dome. That is not part of the test: a mod that retextures the sky must not be able to
    // switch this off, and the counts alone are enough.
    constexpr D3DPRIMITIVETYPE kDomeType = D3DPT_TRIANGLESTRIP;
    constexpr UINT kDomeVertices = 714;
    constexpr UINT kDomePrimitives = 1450;

    // The moon shares the celestial sprites' counts with the sun's flare, and is told from it by
    // its pixel shader: one of the two fogged CelestialBody objects rather than the flare's
    // additive, unfogged pair.
    constexpr UINT kSpriteVertices = 6;
    constexpr UINT kSpritePrimitives = 4;

    // FogValues in the vertex shader, whose third component scales the whole of the sky's fog.
    constexpr UINT kFogValues = 51;

    // The moon's parameters in the pixel shader: time of day, visibility, HDR multiplier, horizon
    // factor. The texture is scaled by visibility times the multiplier, and the world's own
    // multiplier of ten saturates a high, unfogged moon to a white disc.
    constexpr UINT kMoonParams = 71;
    constexpr float kMoonPeak = 1.0f;

    constexpr size_t kDrawPrimitiveSlot = 81;

    // A full-screen pass is one or two triangles, which spares every other draw the shader lookup
    // while only the grade is watched for.
    constexpr UINT kScreenPrimitives = 2;

    // Saturation, ColorRemapData and ContrastData, a register each.
    constexpr UINT kGradeRegisters = 3;

    using DrawIndexedPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, INT,
                                                       UINT, UINT, UINT, UINT);
    using DrawPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, UINT, UINT);

    DrawIndexedPrimitiveFn g_original = nullptr;
    DrawPrimitiveFn g_originalPlain = nullptr;
    SkyOverhaul::DomeDraw::SubstituteFn g_substitute = nullptr;
    SkyOverhaul::DomeDraw::SubstituteFn g_maskSubstitute = nullptr;
    SkyOverhaul::DomeDraw::GradeFn g_grade = nullptr;
    bool g_watchDepth = false;
    SkyOverhaul::DomeDraw::Mode g_mode = SkyOverhaul::DomeDraw::Mode::Engine;

    uint32_t g_substitutions = 0;
    uint32_t g_unfoggedMoons = 0;
    float g_moonVisibility = 0.0f;
    float g_moonMultiplier = 0.0f;

    // Which pass was last drawn into, rather than which frame: a frame can hold more than one sky
    // pass - the world's own and the one behind an open menu - and each of them needs its own sky.
    // No pass has this serial, so the first dome of the session is not mistaken for a second one.
    uint32_t g_drawnPass = 0xFFFFFFFFu;
    uint32_t g_maskedPass = 0xFFFFFFFFu;

    // Everything here is on the path every indexed draw in the process takes, some seventeen
    // hundred of them a frame, so the whole of it is two compares and a mode until a draw matches.
    bool Substitute(IDirect3DDevice9* device, D3DPRIMITIVETYPE type, UINT vertices,
                    UINT primitives) {
        if (primitives != kDomePrimitives || vertices != kDomeVertices || type != kDomeType) {
            return false;
        }
        if (g_mode != SkyOverhaul::DomeDraw::Mode::Overhaul || g_substitute == nullptr) {
            return false;
        }

        // A second dome in one pass would draw over a sky that already covers the viewport, so it
        // is dropped rather than drawn twice. Measurement says there is never a second one; this
        // is what keeps a build that disagrees from blending its sky over itself.
        const uint32_t pass = SkyOverhaul::Frame::PassSerial();
        if (pass == g_drawnPass) {
            return true;
        }

        // Only the pass the world's sky is drawn in, which is the one squeezed against the far
        // plane. Asked after the counts have already matched, so it costs nothing anywhere else.
        D3DVIEWPORT9 viewport = {};
        if (FAILED(device->GetViewport(&viewport)) ||
            viewport.MinZ < SkyOverhaul::Frame::kSkyPassMinZ) {
            return false;
        }

        const bool drawn = g_substitute(device);
        if (drawn) {
            g_drawnPass = pass;
            g_substitutions++;
        }
        return drawn;
    }

    // The mask's clouds are the one strip, measured over whole frames, that blends as zero over the
    // inverse of its own colour. Strips are rare, so the blend is only read for those.
    bool SubstituteMask(IDirect3DDevice9* device, D3DPRIMITIVETYPE type) {
        if (type != D3DPT_TRIANGLESTRIP || g_maskSubstitute == nullptr) {
            return false;
        }
        DWORD source = 0;
        DWORD destination = 0;
        DWORD blend = FALSE;
        device->GetRenderState(D3DRS_SRCBLEND, &source);
        device->GetRenderState(D3DRS_DESTBLEND, &destination);
        device->GetRenderState(D3DRS_ALPHABLENDENABLE, &blend);
        if (source != D3DBLEND_ZERO || destination != D3DBLEND_INVSRCCOLOR || !blend) {
            return false;
        }

        // A second mask draw in one pass would multiply the mask down twice.
        const uint32_t pass = SkyOverhaul::Frame::PassSerial();
        if (pass == g_maskedPass) {
            return true;
        }
        D3DVIEWPORT9 viewport = {};
        if (FAILED(device->GetViewport(&viewport)) ||
            viewport.MinZ < SkyOverhaul::Frame::kSkyPassMinZ) {
            return false;
        }

        const bool drawn = g_maskSubstitute(device);
        if (drawn) {
            g_maskedPass = pass;
        }
        return drawn;
    }

    bool IsMoon(IDirect3DDevice9* device, UINT vertices, UINT primitives) {
        if (primitives != kSpritePrimitives || vertices != kSpriteVertices ||
            g_mode != SkyOverhaul::DomeDraw::Mode::Overhaul) {
            return false;
        }
        D3DVIEWPORT9 viewport = {};
        if (FAILED(device->GetViewport(&viewport)) ||
            viewport.MinZ < SkyOverhaul::Frame::kSkyPassMinZ) {
            return false;
        }
        using SkyOverhaul::KnownShaders::Kind;
        return SkyOverhaul::KnownShaders::Bound(device).kind == Kind::Moon;
    }

    // The draws named by their shaders: one of the world's depth pass is drawn again into the solid
    // depth, a depth reader shows the linear depth texture, and the grade is drawn with our values
    // and its own put back. `draw` is the engine's call.
    template <class Draw>
    HRESULT WatchShaders(IDirect3DDevice9* device, UINT primitives, Draw draw) {
        using SkyOverhaul::KnownShaders::Kind;
        if (SkyOverhaul::SolidDepth::Begin(device)) {
            draw();
            SkyOverhaul::SolidDepth::End(device);
        }
        const bool gradeShape = g_grade != nullptr && primitives <= kScreenPrimitives;
        if (!g_watchDepth && !gradeShape) {
            return draw();
        }
        const SkyOverhaul::KnownShaders::Known known = SkyOverhaul::KnownShaders::Bound(device);
        if (known.kind == Kind::DepthReader && g_watchDepth) {
            SkyOverhaul::DepthTexture::Observe(device);
        }

        float engine[kGradeRegisters * 4] = {};
        float ours[kGradeRegisters * 4] = {};
        if (!gradeShape || known.kind != Kind::Grade ||
            FAILED(device->GetPixelShaderConstantF(known.firstRegister, engine, kGradeRegisters))) {
            return draw();
        }
        g_grade(engine, ours);
        device->SetPixelShaderConstantF(known.firstRegister, ours, kGradeRegisters);
        const HRESULT drawn = draw();
        device->SetPixelShaderConstantF(known.firstRegister, engine, kGradeRegisters);
        return drawn;
    }

    HRESULT __stdcall DrawPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                          UINT startVertex, UINT primitiveCount) {
        return WatchShaders(device, primitiveCount, [&] {
            return g_originalPlain(device, type, startVertex, primitiveCount);
        });
    }

    HRESULT __stdcall DrawIndexedPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                 INT baseVertexIndex, UINT minVertexIndex,
                                                 UINT numVertices, UINT startIndex,
                                                 UINT primitiveCount) {
        if (Substitute(device, type, numVertices, primitiveCount) ||
            SubstituteMask(device, type)) {
            return D3D_OK;
        }

        // The engine fogs the moon by the height of its sprite, which hides all but a high moon in
        // the night's near-black fog; our sky already carries the air's colour in front of it.
        // Without that fog the moon is also held under saturation, so its face stays readable.
        float fog[4] = {};
        float params[4] = {};
        if (IsMoon(device, numVertices, primitiveCount) &&
            SUCCEEDED(device->GetVertexShaderConstantF(kFogValues, fog, 1)) &&
            SUCCEEDED(device->GetPixelShaderConstantF(kMoonParams, params, 1))) {
            g_moonVisibility = params[1];
            g_moonMultiplier = params[2];
            const float peak = params[1] * params[2];
            float held[4] = {params[0], params[1], params[2], params[3]};
            if (peak > kMoonPeak) {
                held[2] *= kMoonPeak / peak;
            }
            const float unfogged[4] = {fog[0], fog[1], 0.0f, fog[3]};
            device->SetVertexShaderConstantF(kFogValues, unfogged, 1);
            device->SetPixelShaderConstantF(kMoonParams, held, 1);
            const HRESULT drawn = g_original(device, type, baseVertexIndex, minVertexIndex,
                                             numVertices, startIndex, primitiveCount);
            device->SetPixelShaderConstantF(kMoonParams, params, 1);
            device->SetVertexShaderConstantF(kFogValues, fog, 1);
            g_unfoggedMoons++;
            return drawn;
        }
        return WatchShaders(device, primitiveCount, [&] {
            return g_original(device, type, baseVertexIndex, minVertexIndex, numVertices,
                              startIndex, primitiveCount);
        });
    }
}

bool SkyOverhaul::DomeDraw::Install(SubstituteFn substitute) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    void* draw = Vtable::Slot(kDrawIndexedPrimitiveSlot);
    if (draw == nullptr) {
        api->Log("dome: no Direct3D 9 device could be created to read the vtable from");
        return false;
    }
    // A rejected hook is already logged by FCSE, naming the plugin that owns the address.
    if (!api->Hook(draw, reinterpret_cast<void*>(&DrawIndexedPrimitiveDetour),
                   reinterpret_cast<void**>(&g_original))) {
        return false;
    }

    g_substitute = substitute;
    FCSE::Logf("dome: watching for %u vertices and %u triangles at the far plane", kDomeVertices,
               kDomePrimitives);

    // The final pass draws its grade through it. Not worth refusing the sky over.
    void* plain = Vtable::Slot(kDrawPrimitiveSlot);
    if (plain == nullptr || !api->Hook(plain, reinterpret_cast<void*>(&DrawPrimitiveDetour),
                                       reinterpret_cast<void**>(&g_originalPlain))) {
        api->Log("dome: DrawPrimitive is not watched, so the grade cannot be changed");
    }
    return true;
}

void SkyOverhaul::DomeDraw::SetMode(Mode mode) {
    g_mode = mode;
}

void SkyOverhaul::DomeDraw::SetMaskSubstitute(SubstituteFn mask) {
    g_maskSubstitute = mask;
}

void SkyOverhaul::DomeDraw::SetGrade(GradeFn grade) {
    g_grade = grade;
}

void SkyOverhaul::DomeDraw::SetWatchDepth(bool watch) {
    g_watchDepth = watch;
}

uint32_t SkyOverhaul::DomeDraw::SubstituteCount() {
    return g_substitutions;
}

uint32_t SkyOverhaul::DomeDraw::UnfoggedMoonCount() {
    return g_unfoggedMoons;
}

void SkyOverhaul::DomeDraw::MoonParameters(float& visibility, float& multiplier) {
    visibility = g_moonVisibility;
    multiplier = g_moonMultiplier;
}
