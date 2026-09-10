#include "engine/dome_draw.h"

#include "engine/frame.h"
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

    using DrawIndexedPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, INT,
                                                       UINT, UINT, UINT, UINT);

    DrawIndexedPrimitiveFn g_original = nullptr;
    SkyOverhaul::DomeDraw::SubstituteFn g_substitute = nullptr;
    SkyOverhaul::DomeDraw::Mode g_mode = SkyOverhaul::DomeDraw::Mode::Engine;

    uint32_t g_substitutions = 0;

    // Which pass was last drawn into, rather than which frame: a frame can hold more than one sky
    // pass - the world's own and the one behind an open menu - and each of them needs its own sky.
    // No pass has this serial, so the first dome of the session is not mistaken for a second one.
    uint32_t g_drawnPass = 0xFFFFFFFFu;

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

    HRESULT __stdcall DrawIndexedPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                 INT baseVertexIndex, UINT minVertexIndex,
                                                 UINT numVertices, UINT startIndex,
                                                 UINT primitiveCount) {
        if (Substitute(device, type, numVertices, primitiveCount)) {
            return D3D_OK;
        }
        return g_original(device, type, baseVertexIndex, minVertexIndex, numVertices, startIndex,
                          primitiveCount);
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
    return true;
}

void SkyOverhaul::DomeDraw::SetMode(Mode mode) {
    g_mode = mode;
}
uint32_t SkyOverhaul::DomeDraw::SubstituteCount() {
    return g_substitutions;
}
