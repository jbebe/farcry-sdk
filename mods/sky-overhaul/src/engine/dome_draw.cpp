#include "engine/dome_draw.h"

#include "engine/com.h"
#include "engine/depth_texture.h"
#include "engine/engine_device.h"
#include "engine/fog_tint.h"
#include "engine/frame.h"
#include "engine/known_shaders.h"
#include "engine/locate.h"
#include "engine/rain_cover.h"
#include "engine/solid_depth.h"
#include "fcse_api.h"

#include <windows.h>

#include <iterator>

namespace {
    // The engine device's two draw methods, __thiscall declared __fastcall. Each sends the shader
    // constants the engine has changed since the last draw to the device, then draws.
    using EngineDrawIndexedFn = HRESULT(__fastcall*)(void* self, void* unused, UINT type,
                                                     INT baseVertexIndex, UINT minVertexIndex,
                                                     UINT numVertices, UINT startIndex,
                                                     UINT primitiveCount);
    using EngineDrawFn = HRESULT(__fastcall*)(void* self, void* unused, UINT type,
                                              UINT startVertex, UINT primitiveCount);
    using FlushFn = void(__cdecl*)(IDirect3DDevice9* device);

    // The patterns leave out the first six bytes, the ones a hook placed by another plugin
    // overwrites.
    FCSE::Relocation<EngineDrawIndexedFn> g_mappedIndexed{FCSE::Uplay(0x00423AF0)};
    FCSE::Relocation<EngineDrawIndexedFn> g_scannedIndexed{FCSE::Pattern(
        "?? ?? ?? ?? ?? ?? 8B 46 38 50 E8 ?? ?? ?? ?? DB 44 24 28 8B 4C 24 28")};
    FCSE::Relocation<EngineDrawFn> g_mappedPlain{FCSE::Uplay(0x00423A70)};
    FCSE::Relocation<EngineDrawFn> g_scannedPlain{FCSE::Pattern(
        "?? ?? ?? ?? ?? ?? 8B 46 38 50 E8 ?? ?? ?? ?? DB 44 24 1C 8B 4C 24 1C")};

    // Where both draws call the constant flush, an E8 and its displacement.
    constexpr size_t kFlushCall = 0x0A;

    // The engine's primitive types as Direct3D's, as its own table maps them in both builds.
    constexpr D3DPRIMITIVETYPE kPrimitiveTypes[] = {
        D3DPT_TRIANGLELIST, D3DPT_TRIANGLESTRIP, D3DPT_TRIANGLESTRIP, D3DPT_LINELIST,
        D3DPT_LINESTRIP,    D3DPT_TRIANGLEFAN,   D3DPT_TRIANGLELIST,
    };

    D3DPRIMITIVETYPE Direct3DType(UINT type) {
        return type < std::size(kPrimitiveTypes) ? kPrimitiveTypes[type] : D3DPT_FORCE_DWORD;
    }

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

    // The star sphere's mesh, which the engine draws through the night with no dome after it once
    // the night is full.
    constexpr UINT kStarVertices = 10320;

    // The moon shares the celestial sprites' counts with the sun's flare, and is told from it by
    // its pixel shader: one of the two fogged CelestialBody objects rather than the flare's
    // additive, unfogged pair.
    constexpr UINT kSpriteVertices = 6;
    constexpr UINT kSpritePrimitives = 4;

    // FogValues in the vertex shader, whose third component scales the whole of the sky's fog.
    constexpr UINT kFogValues = 51;

    // The moon's parameters in the pixel shader: time of day, visibility, HDR multiplier, horizon
    // factor. The texture is scaled by visibility times the multiplier, and the world's own
    // multiplier of ten saturates a high, unfogged moon to a white disc; at most half of one keeps
    // its maria and craters dark on its face.
    constexpr UINT kMoonParams = 71;
    constexpr float kMoonPeak = 0.5f;

    // A full-screen pass is one or two triangles, which spares every other draw the shader lookup
    // while only the grade is watched for.
    constexpr UINT kScreenPrimitives = 2;

    // Saturation, ColorRemapData and ContrastData, a register each.
    constexpr UINT kGradeRegisters = 3;

    // Where our shaders' own registers start, above every register the engine's lit grass and
    // leaves read.
    constexpr UINT kOwnParameters = 110;

    EngineDrawIndexedFn g_original = nullptr;
    EngineDrawFn g_originalPlain = nullptr;
    FlushFn g_flush = nullptr;
    SkyOverhaul::DomeDraw::SubstituteFn g_substitute = nullptr;
    SkyOverhaul::DomeDraw::SubstituteFn g_maskSubstitute = nullptr;
    SkyOverhaul::DomeDraw::GradeFn g_grade = nullptr;
    SkyOverhaul::DomeDraw::ShineFn g_shine = nullptr;
    SkyOverhaul::DomeDraw::FoliageFn g_foliage = nullptr;
    SkyOverhaul::DomeDraw::VertexFn g_vertex = nullptr;
    // A bit per DepthUser that wants the linear depth watched for.
    unsigned g_depthUsers = 0;
    SkyOverhaul::DomeDraw::Mode g_mode = SkyOverhaul::DomeDraw::Mode::Engine;

    // Which pass was last drawn into, rather than which frame: a frame can hold more than one sky
    // pass - the world's own and the one behind an open menu - and each of them needs its own sky.
    // No pass has this serial, so the first dome of the session is not mistaken for a second one.
    uint32_t g_drawnPass = 0xFFFFFFFFu;
    uint32_t g_maskedPass = 0xFFFFFFFFu;
    uint32_t g_starsPass = 0xFFFFFFFFu;

    // Everything here is on the path every indexed draw of the renderer takes, some seventeen
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

    // The engine's draw itself, through the vertex shader g_vertex picks, with the bound one put
    // back after.
    template <class Draw>
    HRESULT DrawVertex(IDirect3DDevice9* device, Draw draw) {
        if (g_vertex == nullptr) {
            return draw();
        }
        D3DVIEWPORT9 viewport = {};
        IDirect3DVertexShader9* bound = nullptr;
        if (FAILED(device->GetViewport(&viewport)) ||
            viewport.MinZ >= SkyOverhaul::Frame::kSkyPassMinZ ||
            FAILED(device->GetVertexShader(&bound)) || bound == nullptr) {
            SkyOverhaul::Release(bound);
            return draw();
        }
        IDirect3DVertexShader9* ours = g_vertex(device, bound);
        if (ours == nullptr) {
            bound->Release();
            return draw();
        }
        device->SetVertexShader(ours);
        const HRESULT drawn = draw();
        device->SetVertexShader(bound);
        bound->Release();
        return drawn;
    }

    // Lit foliage through our vertex shader and its registers, with the engine's put back after.
    template <class Draw>
    HRESULT DrawFoliage(IDirect3DDevice9* device, SkyOverhaul::DomeDraw::FoliageFn light,
                        SkyOverhaul::KnownShaders::VertexKind kind, Draw draw) {
        constexpr UINT kRegisters = SkyOverhaul::DomeDraw::kFoliageParameterRegisters;
        float parameters[kRegisters * 4] = {};
        float engineParameters[kRegisters * 4] = {};
        IDirect3DVertexShader9* engine = nullptr;
        IDirect3DVertexShader9* ours = light(device, kind, parameters);
        if (ours == nullptr || FAILED(device->GetVertexShader(&engine)) ||
            FAILED(device->GetVertexShaderConstantF(kOwnParameters, engineParameters,
                                                    kRegisters))) {
            SkyOverhaul::Release(engine);
            return draw();
        }
        device->SetVertexShader(ours);
        device->SetVertexShaderConstantF(kOwnParameters, parameters, kRegisters);
        const HRESULT drawn = draw();
        device->SetVertexShaderConstantF(kOwnParameters, engineParameters, kRegisters);
        device->SetVertexShader(engine);
        SkyOverhaul::Release(engine);
        return drawn;
    }

    // The draws named by their shaders, each drawn again, replaced or watched as its effect asks.
    // `draw` is the engine's call.
    template <class Draw>
    HRESULT WatchShaders(IDirect3DDevice9* device, UINT primitives, Draw draw) {
        using SkyOverhaul::KnownShaders::Kind;
        using SkyOverhaul::KnownShaders::VertexKind;
        const bool raining = SkyOverhaul::RainCover::Drawn();
        const VertexKind vertex = g_foliage != nullptr || raining
                                      ? SkyOverhaul::KnownShaders::VertexBound(device)
                                      : VertexKind::Other;
        // Leaves let the rain through, so they stay out of the engine's rain cover map.
        if (raining && vertex != VertexKind::Other && SkyOverhaul::RainCover::Drawing(device)) {
            return D3D_OK;
        }
        if (SkyOverhaul::SolidDepth::Begin(device)) {
            draw();
            SkyOverhaul::SolidDepth::End(device);
        }
        if (g_foliage != nullptr && vertex != VertexKind::Other && vertex != VertexKind::Foliage) {
            return DrawFoliage(device, g_foliage, vertex, draw);
        }
        const bool gradeShape = g_grade != nullptr && primitives <= kScreenPrimitives;
        if (g_depthUsers == 0 && !gradeShape && g_shine == nullptr) {
            return draw();
        }
        const SkyOverhaul::KnownShaders::Known known = SkyOverhaul::KnownShaders::Bound(device);
        if (known.kind == Kind::DepthReader && g_depthUsers != 0) {
            SkyOverhaul::DepthTexture::Observe(device);
        }
        if (known.shine != nullptr && g_shine != nullptr) {
            SkyOverhaul::DomeDraw::ShineSaved saved;
            saved.count = 0;
            saved.texture = nullptr;
            g_shine(device, known.shine, saved);
            if (saved.count == 0 && saved.texture == nullptr) {
                return draw();
            }
            const HRESULT drawn = draw();
            for (int i = 0; i < saved.count; i++) {
                device->SetPixelShaderConstantF(saved.registers[i], saved.values[i], 1);
            }
            if (saved.texture != nullptr) {
                device->SetTexture(saved.sampler, saved.texture);
                saved.texture->Release();
            }
            return drawn;
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

    // The device as the draw will find it: the engine's constants sent first, which leaves the
    // flush inside its own draw nothing to send.
    IDirect3DDevice9* Prepare(void* self) {
        IDirect3DDevice9* device = SkyOverhaul::EngineDevice::Direct3D(self);
        g_flush(device);
        SkyOverhaul::FogTint::BeforeDraw(device);
        return device;
    }

    HRESULT __fastcall DrawPrimitiveDetour(void* self, void* unused, UINT engineType,
                                           UINT startVertex, UINT primitiveCount) {
        IDirect3DDevice9* device = Prepare(self);
        return WatchShaders(device, primitiveCount, [&] {
            return DrawVertex(device, [&] {
                return g_originalPlain(self, unused, engineType, startVertex, primitiveCount);
            });
        });
    }

    HRESULT __fastcall DrawIndexedPrimitiveDetour(void* self, void* unused, UINT engineType,
                                                  INT baseVertexIndex, UINT minVertexIndex,
                                                  UINT numVertices, UINT startIndex,
                                                  UINT primitiveCount) {
        IDirect3DDevice9* device = Prepare(self);
        const D3DPRIMITIVETYPE type = Direct3DType(engineType);
        if (Substitute(device, type, numVertices, primitiveCount) ||
            SubstituteMask(device, type)) {
            return D3D_OK;
        }
        if (numVertices == kStarVertices) {
            g_starsPass = SkyOverhaul::Frame::PassSerial();
        }
        const auto engineDraw = [&] {
            return g_original(self, unused, engineType, baseVertexIndex, minVertexIndex,
                              numVertices, startIndex, primitiveCount);
        };

        // The engine fogs the moon by the height of its sprite, which hides all but a high moon in
        // the night's near-black fog; our sky already carries the air's colour in front of it.
        // Without that fog the moon is also held under saturation, so its face stays readable.
        float fog[4] = {};
        float params[4] = {};
        if (IsMoon(device, numVertices, primitiveCount) &&
            SUCCEEDED(device->GetVertexShaderConstantF(kFogValues, fog, 1)) &&
            SUCCEEDED(device->GetPixelShaderConstantF(kMoonParams, params, 1))) {
            const float peak = params[1] * params[2];
            float held[4] = {params[0], params[1], params[2], params[3]};
            if (peak > kMoonPeak) {
                held[2] *= kMoonPeak / peak;
            }
            const float unfogged[4] = {fog[0], fog[1], 0.0f, fog[3]};
            device->SetVertexShaderConstantF(kFogValues, unfogged, 1);
            device->SetPixelShaderConstantF(kMoonParams, held, 1);
            const HRESULT drawn = engineDraw();
            device->SetPixelShaderConstantF(kMoonParams, params, 1);
            device->SetVertexShaderConstantF(kFogValues, fog, 1);
            return drawn;
        }
        return WatchShaders(device, primitiveCount,
                            [&] { return DrawVertex(device, engineDraw); });
    }
}

bool SkyOverhaul::DomeDraw::Install(SubstituteFn substitute) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    const uintptr_t indexed =
        Locate("dome", g_mappedIndexed.address(), g_scannedIndexed.address());
    const uint8_t* call = reinterpret_cast<const uint8_t*>(indexed) + kFlushCall;
    if (indexed == 0 || call[0] != 0xE8) {
        api->Log("dome unavailable: the engine's indexed draw was not found in this build");
        return false;
    }
    g_flush = CallTarget<FlushFn>(call);

    // A rejected hook is already logged by FCSE.
    if (!api->Hook(reinterpret_cast<void*>(indexed),
                   reinterpret_cast<void*>(&DrawIndexedPrimitiveDetour),
                   reinterpret_cast<void**>(&g_original))) {
        return false;
    }

    g_substitute = substitute;

    // The final pass draws its grade through it. Not worth refusing the sky over.
    const uintptr_t plain = Locate("dome", g_mappedPlain.address(), g_scannedPlain.address());
    if (plain == 0 ||
        !api->Hook(reinterpret_cast<void*>(plain), reinterpret_cast<void*>(&DrawPrimitiveDetour),
                   reinterpret_cast<void**>(&g_originalPlain))) {
        api->Log("dome: the engine's plain draw is not watched, so the grade cannot be changed");
    }
    return true;
}

void SkyOverhaul::DomeDraw::SetMode(Mode mode) {
    g_mode = mode;
}

bool SkyOverhaul::DomeDraw::SkyOwed() {
    const uint32_t pass = Frame::PassSerial();
    return g_mode == Mode::Overhaul && g_starsPass == pass && g_drawnPass != pass;
}

void SkyOverhaul::DomeDraw::SetMaskSubstitute(SubstituteFn mask) {
    g_maskSubstitute = mask;
}

void SkyOverhaul::DomeDraw::SetGrade(GradeFn grade) {
    g_grade = grade;
}

void SkyOverhaul::DomeDraw::SetShine(ShineFn shine) {
    g_shine = shine;
}

void SkyOverhaul::DomeDraw::SetFoliage(FoliageFn foliage) {
    g_foliage = foliage;
}

void SkyOverhaul::DomeDraw::SetVertex(VertexFn vertex) {
    g_vertex = vertex;
}

void SkyOverhaul::DomeDraw::SetWatchDepth(DepthUser user, bool watch) {
    const unsigned bit = static_cast<unsigned>(user);
    g_depthUsers = watch ? g_depthUsers | bit : g_depthUsers & ~bit;
}
