#include "engine/weapon_draws.h"

#include "engine/aim.h"
#include "engine/com.h"
#include "engine/render_target.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <algorithm>
#include <cmath>
#include <iterator>

namespace {
    using DrawIndexedPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, INT,
                                                       UINT, UINT, UINT, UINT);

    // A render target that takes no memory and a depth format a shader can read, both driver
    // formats rather than Direct3D's own.
    constexpr D3DFORMAT kNullFormat = static_cast<D3DFORMAT>(MAKEFOURCC('N', 'U', 'L', 'L'));
    constexpr D3DFORMAT kReadableDepth = static_cast<D3DFORMAT>(MAKEFOURCC('I', 'N', 'T', 'Z'));

    // The weapon's depth is drawn through a viewport squeezed this close to the near plane.
    constexpr float kWeaponFarthest = 0.5f;

    // The weapon's projection, unlike the world's, has its near plane a centimetre out.
    constexpr UINT kProjectionRegister = 8;
    constexpr float kWeaponNearest = 0.05f;

    constexpr uint32_t kNone = 0xFFFFFFFFu;

    DrawIndexedPrimitiveFn g_original = nullptr;
    WeaponOverhaul::WeaponDraws::Listener g_listener = {};
    bool g_watching = false;

    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DSurface9* g_nullTarget = nullptr;
    WeaponOverhaul::Target g_depth;
    UINT g_width = 0;
    UINT g_height = 0;
    // Set once the device refuses something; cleared on reset.
    bool g_refused = false;

    // The frame the depth was last cleared for, the pass that began it, and its projection.
    uint32_t g_depthFrame = kNone;
    uint32_t g_depthPass = kNone;
    float g_projection[16] = {};
    uint32_t g_colourFrame = kNone;
    uint32_t g_colourPass = kNone;

    // The parts the gun's depth pass drew this frame, which its colour pass draws again.
    struct Part {
        UINT startIndex;
        UINT primitiveCount;
        UINT numVertices;
    };
    constexpr size_t kMaxParts = 64;
    Part g_parts[kMaxParts] = {};
    size_t g_partCount = 0;
    uint32_t g_partsFrame = kNone;

    void Remember(uint32_t frame, const Part& part) {
        if (g_partsFrame != frame) {
            g_partsFrame = frame;
            g_partCount = 0;
        }
        if (g_partCount < kMaxParts) {
            g_parts[g_partCount++] = part;
        }
    }

    bool IsGunPart(uint32_t frame, const Part& part) {
        if (g_partsFrame != frame) {
            return false;
        }
        for (size_t i = 0; i < g_partCount; i++) {
            if (g_parts[i].startIndex == part.startIndex &&
                g_parts[i].primitiveCount == part.primitiveCount &&
                g_parts[i].numVertices == part.numVertices) {
                return true;
            }
        }
        return false;
    }

    // What a gathered draw changes, put back after it.
    constexpr DWORD kTargets = 4;
    IDirect3DSurface9* g_savedTargets[kTargets] = {};
    IDirect3DSurface9* g_savedDepth = nullptr;
    D3DVIEWPORT9 g_savedViewport = {};
    RECT g_savedScissor = {};
    constexpr D3DRENDERSTATETYPE kStates[] = {D3DRS_ZENABLE, D3DRS_ZFUNC, D3DRS_STENCILENABLE};
    constexpr DWORD kGatherStates[] = {D3DZB_TRUE, D3DCMP_LESSEQUAL, FALSE};
    DWORD g_savedStates[std::size(kStates)] = {};

    void ReleaseTargets() {
        WeaponOverhaul::Release(g_nullTarget);
        WeaponOverhaul::Release(g_depth);
        g_depthFrame = kNone;
    }

    bool Refuse(const char* what, HRESULT result) {
        ReleaseTargets();
        g_refused = true;
        FCSE::Logf("weapon depth: the device refused the %s, 0x%08lX", what,
                   static_cast<unsigned long>(result));
        return false;
    }

    bool EnsureTargets(IDirect3DDevice9* device, UINT width, UINT height) {
        if (g_owner == device && g_nullTarget != nullptr && g_width == width &&
            g_height == height) {
            return true;
        }
        ReleaseTargets();
        if (g_refused) {
            return false;
        }
        g_owner = device;
        HRESULT created = WeaponOverhaul::CreateTarget(device, width, height, kReadableDepth,
                                                       g_depth, D3DUSAGE_DEPTHSTENCIL);
        if (FAILED(created)) {
            return Refuse("INTZ depth texture", created);
        }
        if (FAILED(created = device->CreateRenderTarget(width, height, kNullFormat,
                                                        D3DMULTISAMPLE_NONE, 0, FALSE,
                                                        &g_nullTarget, nullptr))) {
            return Refuse("NULL render target", created);
        }
        g_width = width;
        g_height = height;
        return true;
    }

    // The projection a draw is about to use, if it is the weapon's.
    bool ReadGunProjection(IDirect3DDevice9* device, float (&projection)[16]) {
        if (FAILED(device->GetVertexShaderConstantF(kProjectionRegister, projection, 4)) ||
            std::abs(projection[14]) != 1.0f || projection[15] != 0.0f || projection[10] == 0.0f) {
            return false;
        }
        const float nearest = -projection[11] / (projection[10] * projection[14]);
        return nearest > 0.0f && nearest < kWeaponNearest;
    }

    // Binds our depth for one of the gun's depth draws, at half size over the whole depth range,
    // with every target the engine had bound taken off.
    bool Begin(IDirect3DDevice9* device, const D3DVIEWPORT9& viewport, const float* projection) {
        DWORD writes = FALSE;
        if (viewport.Width != WeaponOverhaul::Frame::Width() ||
            viewport.Height != WeaponOverhaul::Frame::Height() ||
            FAILED(device->GetRenderState(D3DRS_ZWRITEENABLE, &writes)) || writes == FALSE ||
            !EnsureTargets(device, (viewport.Width + 1) / 2, (viewport.Height + 1) / 2)) {
            return false;
        }

        g_savedViewport = viewport;
        device->GetScissorRect(&g_savedScissor);
        for (DWORD i = 0; i < kTargets; i++) {
            device->GetRenderTarget(i, &g_savedTargets[i]);
        }
        device->GetDepthStencilSurface(&g_savedDepth);
        for (size_t i = 0; i < std::size(kStates); i++) {
            device->GetRenderState(kStates[i], &g_savedStates[i]);
            device->SetRenderState(kStates[i], kGatherStates[i]);
        }
        device->SetRenderTarget(0, g_nullTarget);
        for (DWORD i = 1; i < kTargets; i++) {
            if (g_savedTargets[i] != nullptr) {
                device->SetRenderTarget(i, nullptr);
            }
        }
        device->SetDepthStencilSurface(g_depth.surface);

        const uint32_t frame = WeaponOverhaul::Frame::Number();
        if (g_depthFrame != frame) {
            device->Clear(0, nullptr, D3DCLEAR_ZBUFFER | D3DCLEAR_STENCIL, 0, 1.0f, 0);
            g_depthFrame = frame;
            g_depthPass = WeaponOverhaul::Frame::PassSerial();
            std::copy_n(projection, std::size(g_projection), g_projection);
        }
        const D3DVIEWPORT9 half = {viewport.X / 2, viewport.Y / 2, g_width, g_height, 0.0f, 1.0f};
        device->SetViewport(&half);
        return true;
    }

    void End(IDirect3DDevice9* device) {
        for (DWORD i = 0; i < kTargets; i++) {
            if (i == 0 || g_savedTargets[i] != nullptr) {
                device->SetRenderTarget(i, g_savedTargets[i]);
            }
            WeaponOverhaul::Release(g_savedTargets[i]);
        }
        device->SetDepthStencilSurface(g_savedDepth);
        device->SetViewport(&g_savedViewport);
        device->SetScissorRect(&g_savedScissor);
        for (size_t i = 0; i < std::size(kStates); i++) {
            device->SetRenderState(kStates[i], g_savedStates[i]);
        }
        WeaponOverhaul::Release(g_savedDepth);
    }

    HRESULT __stdcall DrawIndexedPrimitiveDetour(IDirect3DDevice9* device, D3DPRIMITIVETYPE type,
                                                 INT baseVertexIndex, UINT minVertexIndex,
                                                 UINT numVertices, UINT startIndex,
                                                 UINT primitiveCount) {
        const auto draw = [&] {
            return g_original(device, type, baseVertexIndex, minVertexIndex, numVertices,
                              startIndex, primitiveCount);
        };
        D3DVIEWPORT9 viewport = {};
        float projection[16] = {};
        const bool squeezed = !WeaponOverhaul::Frame::PastSky();
        if (!g_watching || FAILED(device->GetViewport(&viewport)) ||
            (viewport.MaxZ < kWeaponFarthest) != squeezed ||
            !ReadGunProjection(device, projection)) {
            return draw();
        }

        const uint32_t frame = WeaponOverhaul::Frame::Number();
        const Part part = {startIndex, primitiveCount, numVertices};
        if (!g_listener.beforeDraw(device, projection, squeezed)) {
            return D3D_OK;
        }
        if (squeezed) {
            if (Begin(device, viewport, projection)) {
                Remember(frame, part);
                draw();
                End(device);
            }
        } else if (g_colourFrame != frame && IsGunPart(frame, part)) {
            g_colourFrame = frame;
            g_colourPass = WeaponOverhaul::Frame::PassSerial();
        }
        const HRESULT result = draw();
        g_listener.afterDraw(device);
        return result;
    }

    void OnScenePass(const WeaponOverhaul::Frame::Pass& pass) {
        g_watching = WeaponOverhaul::Aim::Settled() > 0.0f || WeaponOverhaul::Aim::Scoped() > 0.0f ||
                     WeaponOverhaul::Aim::ScopeUp();
        if (!g_watching || g_depthFrame != WeaponOverhaul::Frame::Number()) {
            return;
        }
        const WeaponOverhaul::WeaponDraws::Depth depth = {
            g_depth.texture, g_width, g_height, g_projection[5],
            g_projection[10] * g_projection[14], g_projection[11]};
        if (pass.serial == g_depthPass) {
            g_listener.onDepthPass(pass, depth);
        }
        if (pass.serial == g_colourPass) {
            g_listener.onGunPass(pass, depth);
        }
    }
}

bool WeaponOverhaul::WeaponDraws::Install(const Listener& listener) {
    g_listener = listener;
    return Frame::Install(&OnScenePass) &&
           Vtable::Hook(Vtable::kDrawIndexedPrimitive,
                        reinterpret_cast<void*>(&DrawIndexedPrimitiveDetour),
                        reinterpret_cast<void**>(&g_original));
}

void WeaponOverhaul::WeaponDraws::ReleaseDeviceObjects() {
    ReleaseTargets();
    g_owner = nullptr;
    g_refused = false;
}
