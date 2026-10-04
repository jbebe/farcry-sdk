#include "engine/weapon_draws.h"

#include "engine/aim.h"
#include "engine/com.h"
#include "engine/render_target.h"
#include "engine/vtable.h"
#include "fcse_api.h"

#include <cmath>
#include <iterator>

namespace {
    using DrawIndexedPrimitiveFn = HRESULT(__stdcall*)(IDirect3DDevice9*, D3DPRIMITIVETYPE, INT,
                                                       UINT, UINT, UINT, UINT);
    using WeaponOverhaul::Frame::kNever;
    using WeaponOverhaul::WeaponDraws::Projection;

    // A render target that takes no memory and a depth format a shader can read, both driver
    // formats rather than Direct3D's own.
    constexpr D3DFORMAT kNullFormat = static_cast<D3DFORMAT>(MAKEFOURCC('N', 'U', 'L', 'L'));
    constexpr D3DFORMAT kReadableDepth = static_cast<D3DFORMAT>(MAKEFOURCC('I', 'N', 'T', 'Z'));

    // The weapon's depth is drawn through a viewport squeezed this close to the near plane.
    constexpr float kWeaponFarthest = 0.5f;

    // Where the projection is, and the farthest out the weapon's near plane is taken to be; the
    // world's is further.
    constexpr UINT kProjectionRegister = 8;
    constexpr float kWeaponNearest = 0.05f;

    DrawIndexedPrimitiveFn g_original = nullptr;
    WeaponOverhaul::WeaponDraws::Listener g_listener = {};
    // Whether the gun's draws are watched; and the frame a scope's sight picture was last up in,
    // which the render thread can draw frames behind, so the gun is watched for a few frames more.
    bool g_watching = false;
    uint32_t g_scopedFrame = kNever;
    constexpr uint32_t kLingerFrames = 8;

    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DSurface9* g_nullTarget = nullptr;
    WeaponOverhaul::Target g_depth;
    UINT g_width = 0;
    UINT g_height = 0;
    // Set once the device refuses something; cleared on reset.
    bool g_refused = false;

    // The frame the depth was last cleared for, and its projection.
    uint32_t g_depthFrame = kNever;
    Projection g_projection = {};
    // The frame the gun's colour last began in, and the frame and pass it was recognised in.
    uint32_t g_colourStart = kNever;
    uint32_t g_colourFrame = kNever;
    uint32_t g_colourPass = kNever;

    // The parts the gun's depth pass drew this frame, which its colour pass draws again.
    struct Part {
        UINT startIndex;
        UINT primitiveCount;
        UINT numVertices;
    };
    constexpr size_t kMaxParts = 64;
    Part g_parts[kMaxParts] = {};
    size_t g_partCount = 0;
    uint32_t g_partsFrame = kNever;

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
        g_depthFrame = kNever;
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

    // The projection a draw is about to use, if it is the weapon's: one register a row, the last
    // making clip space's w.
    bool ReadGunProjection(IDirect3DDevice9* device, Projection& projection) {
        float rows[16] = {};
        if (FAILED(device->GetVertexShaderConstantF(kProjectionRegister, rows, 4)) ||
            std::abs(rows[14]) != 1.0f || rows[15] != 0.0f || rows[10] == 0.0f) {
            return false;
        }
        projection = {rows[5], rows[10] * rows[14], rows[11]};
        const float nearest = -projection.depthOffset / projection.depthScale;
        return nearest > 0.0f && nearest < kWeaponNearest;
    }

    // Binds our depth for one of the gun's depth draws, at half size over the whole depth range,
    // with every target the engine had bound taken off.
    bool Begin(IDirect3DDevice9* device, const D3DVIEWPORT9& viewport,
               const Projection& projection) {
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
            g_projection = projection;
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
        if (!g_watching) {
            return draw();
        }
        D3DVIEWPORT9 viewport = {};
        Projection projection = {};
        const bool squeezed = !WeaponOverhaul::Frame::PastSky();
        if (FAILED(device->GetViewport(&viewport)) ||
            (viewport.MaxZ < kWeaponFarthest) != squeezed ||
            !ReadGunProjection(device, projection)) {
            return draw();
        }

        const uint32_t frame = WeaponOverhaul::Frame::Number();
        const Part part = {startIndex, primitiveCount, numVertices};
        if (squeezed) {
            if (Begin(device, viewport, projection)) {
                Remember(frame, part);
                draw();
                End(device);
            }
        } else {
            if (g_colourStart != frame) {
                g_colourStart = frame;
                g_listener.beforeColour(device);
            }
            if (g_colourFrame != frame && IsGunPart(frame, part)) {
                g_colourFrame = frame;
                g_colourPass = WeaponOverhaul::Frame::PassSerial();
            }
        }
        if (!g_listener.beforeDraw(device, {primitiveCount, numVertices, squeezed})) {
            return D3D_OK;
        }
        return draw();
    }

    WeaponOverhaul::WeaponDraws::Depth ThisDepth() {
        return {g_depth.texture, g_width, g_height, g_projection};
    }

    void OnScenePass(const WeaponOverhaul::Frame::Pass& pass) {
        const uint32_t frame = WeaponOverhaul::Frame::Number();
        if (WeaponOverhaul::Aim::ScopeUp()) {
            g_scopedFrame = frame;
        }
        g_watching = WeaponOverhaul::Aim::Aiming() ||
                     WeaponOverhaul::Frame::Within(g_scopedFrame, kLingerFrames);
        if (!g_watching || g_depthFrame != frame) {
            return;
        }
        if (pass.serial == g_colourPass) {
            g_listener.onGunPass(pass, ThisDepth());
        }
    }

    void OnComposite(IDirect3DDevice9* device) {
        if (g_watching && g_depthFrame == WeaponOverhaul::Frame::Number()) {
            g_listener.onComposite(device);
        }
    }
}

bool WeaponOverhaul::WeaponDraws::Install(const Listener& listener) {
    g_listener = listener;
    return Frame::Install(&OnScenePass, &OnComposite) &&
           Vtable::Hook(Vtable::kDrawIndexedPrimitive,
                        reinterpret_cast<void*>(&DrawIndexedPrimitiveDetour),
                        reinterpret_cast<void**>(&g_original));
}

void WeaponOverhaul::WeaponDraws::ReleaseDeviceObjects() {
    ReleaseTargets();
    g_owner = nullptr;
    g_refused = false;
}
