// The engine's scope is dropped once its housing's draw says which scope it is; the composite then
// draws that scope's reticle from the textures the engine drew it with, and the body over it.
#include "eyepiece.h"

#include "engine/aim.h"
#include "engine/com.h"
#include "engine/frame.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "scopes.h"

#include "eyepiece_body_ps.h"
#include "eyepiece_reticle_ps.h"

#include <algorithm>
#include <atomic>
#include <cstring>
#include <iterator>
#include <vector>

namespace {
    using WeaponOverhaul::Scopes::Scope;

    // The body as it is drawn for every scope: its outer edge's radius, the mount's half-width and
    // its softness, as shares of the screen's height.
    constexpr float kBodyRadius = 0.4f;
    constexpr float kMountHalfWidth = 0.21f;
    constexpr float kBodySoftness = 0.0125f;
    // How far inside the opening the body is wholly black, so that its soft edge lies over the
    // shadow and the reticle rather than leaving a light seam between them and it.
    constexpr float kBodyOverlap = 0.005f;
    // How far the scope trails the look's turn, as a share of the shadow's swing.
    constexpr float kTrail = 0.1f;

    WeaponOverhaul::PixelShader g_reticleShader{"eyepiece reticle", g_eyepieceReticlePixelShader};
    WeaponOverhaul::PixelShader g_bodyShader{"eyepiece body", g_eyepieceBodyPixelShader};

    std::atomic<bool> g_enabled{true};

    // The most pieces a reticle has.
    constexpr size_t kMostPieces = 4;

    // One scope-up, from the engine's draws. Vertex buffers are compared and never dereferenced.
    struct ScopeUp {
        uint32_t number;
        // The scope, once its housing's draw is known, and the vertex buffer it draws from.
        const Scope* scope;
        const void* vertices;
        // Until then, the vertex buffer an alpha-tested draw used, and the size of its last other
        // draw in the gun's depth pass, which a scope's housing would be.
        const void* stranger;
        UINT primitives;
        UINT numVertices;
        bool logged;
        // The texture each piece of the reticle was drawn with, and the frame it was.
        IDirect3DBaseTexture9* textures[kMostPieces];
        uint32_t textureFrames[kMostPieces];
    };
    ScopeUp g_up = {};

    // The distance field of the scope it was made for, on the device that owns it.
    IDirect3DDevice9* g_shapeOwner = nullptr;
    const Scope* g_shapeOf = nullptr;
    IDirect3DTexture9* g_shape = nullptr;

    // A COM pointer without the reference its getter added.
    template <typename T>
    T* Borrowed(T* object) {
        if (object != nullptr) {
            object->Release();
        }
        return object;
    }

    // Starts over each time a scope comes up.
    void Follow() {
        const uint32_t number = WeaponOverhaul::Aim::ScopeUps();
        if (g_up.number != number) {
            g_up = {};
            g_up.number = number;
            std::fill(std::begin(g_up.textureFrames), std::end(g_up.textureFrames),
                      WeaponOverhaul::Frame::kNever);
        }
    }

    // How far the eye comes forward as the weapon in hand raises its scope.
    void FollowRaise(const WeaponOverhaul::WeaponDraws::Call& call) {
        if (const Scope* scope = WeaponOverhaul::Scopes::InHand(call.numVertices)) {
            WeaponOverhaul::Aim::SetRaiseReach(scope->raiseReach);
        }
    }

    // Names a scope that is drawn as the game draws it for want of being known, once a scope-up.
    void LogStranger() {
        if (g_up.scope != nullptr || g_up.primitives == 0 || g_up.logged) {
            return;
        }
        g_up.logged = true;
        FCSE::Logf("eyepiece: a scope whose housing draws %u primitives over %u vertices is not "
                   "known, so it is drawn as the game draws it",
                   g_up.primitives, g_up.numVertices);
    }

    // The scope's own shape, made the first time it is asked for; null for a plain ring.
    IDirect3DTexture9* ShapeFor(IDirect3DDevice9* device, const Scope& scope) {
        if (scope.shape.empty() || (g_shapeOwner == device && g_shapeOf == &scope)) {
            return scope.shape.empty() ? nullptr : g_shape;
        }
        WeaponOverhaul::Release(g_shape);
        g_shapeOwner = device;
        g_shapeOf = &scope;
        constexpr UINT size = WeaponOverhaul::Scopes::kShapeSize;
        D3DLOCKED_RECT locked = {};
        if (FAILED(device->CreateTexture(size, size, 1, 0, D3DFMT_L8, D3DPOOL_MANAGED, &g_shape,
                                         nullptr)) ||
            FAILED(g_shape->LockRect(0, &locked, nullptr, 0))) {
            WeaponOverhaul::Release(g_shape);
            FCSE::ApiPointer()->Log("eyepiece: the device refused a scope's shape");
            return nullptr;
        }
        for (UINT row = 0; row < size; row++) {
            std::memcpy(static_cast<BYTE*>(locked.pBits) + row * locked.Pitch,
                        scope.shape.data() + row * size, size);
        }
        g_shape->UnlockRect(0);
        return g_shape;
    }

    // The scope's reticle, each piece from the texture the engine drew it with this frame, placed
    // about the look's centre as the opening is.
    void DrawReticle(IDirect3DDevice9* device, WeaponOverhaul::ScreenDraw& draw, const Scope& scope,
                     const WeaponOverhaul::Eyepiece::Opening& opening) {
        IDirect3DPixelShader9* shader = g_reticleShader.Get(device);
        if (shader == nullptr) {
            return;
        }
        const float width = static_cast<float>(WeaponOverhaul::Frame::Width());
        const float height = static_cast<float>(WeaponOverhaul::Frame::Height());
        const float scale = opening.radius * height;
        const float centreX = width / 2.0f + opening.x * height - scope.lensX * scale;
        const float centreY = height / 2.0f + opening.y * height + scope.lensY * scale;

        device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(0, D3DSAMP_MIPFILTER, D3DTEXF_LINEAR);
        device->SetPixelShader(shader);
        std::vector<WeaponOverhaul::ScreenDraw::Vertex> vertices;
        for (size_t i = 0; i < scope.pieces.size() && i < kMostPieces; i++) {
            const WeaponOverhaul::Scopes::Piece& piece = scope.pieces[i];
            if (g_up.textureFrames[i] != WeaponOverhaul::Frame::Number()) {
                continue;
            }
            device->SetTexture(0, g_up.textures[i]);
            const float lit = piece.look == WeaponOverhaul::Scopes::Look::Lit ? 1.0f : 0.0f;
            const float look[4] = {lit, lit, lit, 1.0f - lit};
            device->SetPixelShaderConstantF(0, look, 1);
            vertices.clear();
            for (const WeaponOverhaul::Scopes::Point& point : piece.triangles) {
                vertices.push_back(
                    {centreX + point.x * scale, centreY - point.y * scale, point.u, point.v});
            }
            draw.Triangles(vertices.data(), static_cast<UINT>(vertices.size()));
        }
    }
}

bool WeaponOverhaul::Eyepiece::BeforeGunDraw(IDirect3DDevice9* device,
                                             const WeaponDraws::Call& call) {
    if (!g_enabled) {
        return true;
    }
    if (!Aim::ScopeUp()) {
        if (call.depthPass) {
            FollowRaise(call);
        }
        return true;
    }
    Follow();
    IDirect3DVertexBuffer9* stream = nullptr;
    UINT offset = 0;
    UINT stride = 0;
    device->GetStreamSource(0, &stream, &offset, &stride);
    const void* vertices = Borrowed(stream);
    DWORD alphaTest = FALSE;
    device->GetRenderState(D3DRS_ALPHATESTENABLE, &alphaTest);

    if (g_up.scope == nullptr) {
        if (alphaTest) {
            g_up.stranger = vertices;
        } else if (call.depthPass) {
            if ((g_up.scope = Scopes::Find(call.primitiveCount, call.numVertices)) != nullptr) {
                g_up.vertices = vertices;
            } else if (vertices == g_up.stranger) {
                g_up.primitives = call.primitiveCount;
                g_up.numVertices = call.numVertices;
            }
        }
    }
    if (g_up.scope == nullptr || vertices != g_up.vertices) {
        return true;
    }
    // A piece of the reticle is told by its draw's size and whether the engine alpha-tests it.
    const std::span<const Scopes::Piece> pieces = g_up.scope->pieces;
    for (size_t i = 0; i < pieces.size() && i < kMostPieces; i++) {
        if (pieces[i].triangles.size() / 3 == call.primitiveCount &&
            (pieces[i].look == Scopes::Look::Black) == (alphaTest != FALSE)) {
            IDirect3DBaseTexture9* texture = nullptr;
            device->GetTexture(0, &texture);
            g_up.textures[i] = Borrowed(texture);
            g_up.textureFrames[i] = Frame::Number();
        }
    }
    return false;
}

void WeaponOverhaul::Eyepiece::OnComposite(IDirect3DDevice9* device) {
    LogStranger();
    const std::optional<Opening> opening = Open();
    IDirect3DPixelShader9* body = opening ? g_bodyShader.Get(device) : nullptr;
    if (body == nullptr) {
        return;
    }
    const Scope& scope = *g_up.scope;
    IDirect3DTexture9* shape = ShapeFor(device, scope);
    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    const float radius = opening->radius;
    // c1 to c3: the lens on screen, the body in lens radii, and where the shape is from the lens.
    const float constants[12] = {
        width / height, opening->x, opening->y, radius,
        1.0f - (kBodyOverlap + kBodySoftness) / radius, 1.0f + opening->rim,
        kMountHalfWidth / radius, kBodySoftness / radius,
        -scope.lensX, scope.lensY, shape != nullptr ? 2.0f * Scopes::kShapeReach : 0.0f,
        Scopes::kShapeSpan,
    };

    ScreenDraw draw(device, 0, 4);
    // Each lays its colour over what is there by how much it covers it.
    device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
    device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
    device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                       D3DCOLORWRITEENABLE_GREEN |
                                                       D3DCOLORWRITEENABLE_BLUE);
    device->SetPixelShaderConstantF(1, constants, 3);
    DrawReticle(device, draw, scope, *opening);

    device->SetTexture(1, shape);
    device->SetSamplerState(1, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    device->SetSamplerState(1, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    device->SetPixelShader(body);
    draw.Quad(0.0f, 0.0f, width, height);
}

std::optional<WeaponOverhaul::Eyepiece::Opening> WeaponOverhaul::Eyepiece::Open() {
    if (!g_enabled || !Aim::ScopeUp() || g_up.number != Aim::ScopeUps() || g_up.scope == nullptr) {
        return std::nullopt;
    }
    const Scope& scope = *g_up.scope;
    const float radius = kBodyRadius / (1.0f + scope.rim);
    const Aim::Swing swing = Aim::ScopeSwing();
    return Opening{(kTrail * swing.x + scope.lensX) * radius,
                   (kTrail * swing.y - scope.lensY) * radius, radius, scope.rim};
}

void WeaponOverhaul::Eyepiece::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::Eyepiece::ReleaseDeviceObjects() {
    g_reticleShader.Release();
    g_bodyShader.Release();
    Release(g_shape);
    g_shapeOwner = nullptr;
    g_shapeOf = nullptr;
    std::fill(std::begin(g_up.textureFrames), std::end(g_up.textureFrames), Frame::kNever);
}
