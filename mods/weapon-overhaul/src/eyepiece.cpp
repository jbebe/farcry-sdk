// The engine's scope is dropped once the weapon in hand says which scope it is; the composite then
// redraws the view inside the opening through the scope's glass, draws the scope's reticle from the
// textures the engine drew it with, and the body over both.
#include "eyepiece.h"

#include "engine/aim.h"
#include "engine/com.h"
#include "engine/frame.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "scopes.h"

#include "eyepiece_body_ps.h"
#include "eyepiece_reticle_ps.h"
#include "glass_ps.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstring>
#include <iterator>
#include <numbers>
#include <vector>

namespace {
    using WeaponOverhaul::Scopes::Scope;

    // The mount's half-width and the body's softness, as shares of the screen's height.
    constexpr float kMountHalfWidth = 0.21f;
    constexpr float kBodySoftness = 0.0125f;
    // How far inside the opening the body is wholly black, so that its soft edge lies over the
    // shadow and the reticle rather than leaving a light seam between them and it.
    constexpr float kBodyOverlap = 0.005f;
    // How far the scope trails the look's turn, as a share of the shadow's swing.
    constexpr float kTrail = 0.1f;
    // The eyepiece outlasts the sight picture by a frame: the frame it goes in can still be drawn
    // at the scope's field of view.
    constexpr uint32_t kHeldFrames = 1;

    // The glass, at the lens's rim: how far the view is pulled in, how far red and blue part from
    // green for each time the scope magnifies, how much the view darkens and how brightly the
    // smudges glow; and what the coating lets through, a little less light with a faint green.
    constexpr float kPincushion = 0.06f;
    constexpr float kFringePerMagnification = 0.0015f;
    constexpr float kRimDarkening = 0.35f;
    constexpr float kSmudgeGlow = 0.12f;
    constexpr float kTint[3] = {0.95f, 0.97f, 0.96f};
    // The smudge texture's side, and how many smudges it has.
    constexpr UINT kDirtSize = 256;
    constexpr int kSmudges = 28;

    WeaponOverhaul::PixelShader g_reticleShader{"eyepiece reticle", g_eyepieceReticlePixelShader};
    WeaponOverhaul::PixelShader g_bodyShader{"eyepiece body", g_eyepieceBodyPixelShader};
    WeaponOverhaul::PixelShader g_glassShader{"eyepiece glass", g_glassPixelShader};

    // A copy of the finished frame for the glass to look through, and the glass's smudges, on the
    // devices that made them; each refused until a reset if the device would not make it.
    IDirect3DDevice9* g_copyOwner = nullptr;
    WeaponOverhaul::Target g_copy;
    D3DSURFACE_DESC g_copyDesc = {};
    bool g_copyRefused = false;
    IDirect3DDevice9* g_dirtOwner = nullptr;
    IDirect3DTexture9* g_dirt = nullptr;

    std::atomic<bool> g_enabled{true};

    // The scope of the weapon in hand, the vertex buffer the weapon draws from once one of its
    // draws has shown it, compared and never dereferenced, and the change of weapon they are for;
    // and the change a weapon with an unknown scope was last named for.
    const Scope* g_inHand = nullptr;
    const void* g_inHandVertices = nullptr;
    uint32_t g_inHandFor = WeaponOverhaul::Frame::kNever;
    uint32_t g_strangerFor = WeaponOverhaul::Frame::kNever;

    // The frame the weapon in hand last drew its scope's sight picture in, which the render thread
    // can draw a frame after the game has put it away.
    uint32_t g_drawnFrame = WeaponOverhaul::Frame::kNever;

    // The most pieces a reticle has, and the texture each was last drawn with, and when.
    constexpr size_t kMostPieces = 2;
    struct Seen {
        IDirect3DBaseTexture9* texture = nullptr;
        uint32_t frame = WeaponOverhaul::Frame::kNever;
    };
    Seen g_seen[kMostPieces];

    // The distance field of the scope it was made for, on the device that owns it.
    IDirect3DDevice9* g_shapeOwner = nullptr;
    const Scope* g_shapeOf = nullptr;
    IDirect3DTexture9* g_shape = nullptr;

    bool Held(uint32_t frame) {
        return WeaponOverhaul::Frame::Within(frame, kHeldFrames);
    }

    // The scope of the weapon in hand.
    void FollowWeapon() {
        const uint32_t changes = WeaponOverhaul::Aim::WeaponChanges();
        if (changes != g_inHandFor) {
            g_inHandFor = changes;
            g_inHand = WeaponOverhaul::Scopes::Find(WeaponOverhaul::Aim::WeaponName());
            g_inHandVertices = nullptr;
        }
    }

    // Names a weapon whose scope is drawn as the game draws it for want of being known, once each
    // time it is taken in hand.
    void LogStranger() {
        if (g_strangerFor != g_inHandFor) {
            g_strangerFor = g_inHandFor;
            FCSE::Logf("eyepiece: the scope on \"%s\" is not known, so it is drawn as the game "
                       "draws it",
                       WeaponOverhaul::Aim::WeaponName());
        }
    }

    // The scope's own shape, made the first time it is asked for; null for a plain ring.
    IDirect3DTexture9* ShapeFor(IDirect3DDevice9* device, const Scope& scope) {
        if (scope.shape.empty()) {
            return nullptr;
        }
        if (g_shapeOwner == device && g_shapeOf == &scope) {
            return g_shape;
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

    // Smudges on the eyepiece's glass: soft ovals, some wiped long, scattered over it the same way
    // every time. Made once a device.
    IDirect3DTexture9* DirtFor(IDirect3DDevice9* device) {
        if (g_dirtOwner == device) {
            return g_dirt;
        }
        g_dirtOwner = device;
        D3DLOCKED_RECT locked = {};
        if (FAILED(device->CreateTexture(kDirtSize, kDirtSize, 1, 0, D3DFMT_L8, D3DPOOL_MANAGED,
                                         &g_dirt, nullptr)) ||
            FAILED(g_dirt->LockRect(0, &locked, nullptr, 0))) {
            WeaponOverhaul::Release(g_dirt);
            FCSE::ApiPointer()->Log("eyepiece: the device refused the glass's smudges");
            return nullptr;
        }
        std::vector<float> density(kDirtSize * kDirtSize, 0.0f);
        uint32_t seed = 0x2F6E2B1u;
        const auto next = [&seed] {
            seed = seed * 1664525u + 1013904223u;
            return static_cast<float>(seed >> 8) / 16777216.0f;
        };
        for (int i = 0; i < kSmudges; i++) {
            const float centreX = next() * kDirtSize;
            const float centreY = next() * kDirtSize;
            const float radius = (0.02f + 0.08f * next()) * kDirtSize;
            const float stretch = 1.0f + 3.0f * next();
            const float angle = 2.0f * std::numbers::pi_v<float> * next();
            const float strength = 0.15f + 0.5f * next();
            const float along = std::cos(angle);
            const float across = std::sin(angle);
            const float reach = 3.0f * radius * stretch;
            for (int y = (std::max)(0, static_cast<int>(centreY - reach));
                 y < (std::min)(static_cast<int>(kDirtSize), static_cast<int>(centreY + reach));
                 y++) {
                for (int x = (std::max)(0, static_cast<int>(centreX - reach));
                     x < (std::min)(static_cast<int>(kDirtSize), static_cast<int>(centreX + reach));
                     x++) {
                    const float dx = x - centreX;
                    const float dy = y - centreY;
                    const float u = (dx * along + dy * across) / stretch;
                    const float v = -dx * across + dy * along;
                    density[y * kDirtSize + x] +=
                        strength * std::exp(-2.0f * (u * u + v * v) / (radius * radius));
                }
            }
        }
        for (UINT y = 0; y < kDirtSize; y++) {
            BYTE* row = static_cast<BYTE*>(locked.pBits) + y * locked.Pitch;
            for (UINT x = 0; x < kDirtSize; x++) {
                row[x] = static_cast<BYTE>((std::min)(density[y * kDirtSize + x], 1.0f) * 255.0f);
            }
        }
        g_dirt->UnlockRect(0);
        return g_dirt;
    }

    bool EnsureCopy(IDirect3DDevice9* device, IDirect3DSurface9* frame) {
        D3DSURFACE_DESC desc = {};
        frame->GetDesc(&desc);
        if (g_copyOwner == device && g_copy.texture != nullptr && g_copyDesc.Width == desc.Width &&
            g_copyDesc.Height == desc.Height && g_copyDesc.Format == desc.Format) {
            return true;
        }
        WeaponOverhaul::Release(g_copy);
        if (g_copyRefused) {
            return false;
        }
        g_copyOwner = device;
        g_copyDesc = desc;
        if (FAILED(WeaponOverhaul::CreateTarget(device, desc.Width, desc.Height, desc.Format,
                                                g_copy))) {
            WeaponOverhaul::Release(g_copy);
            g_copyRefused = true;
            FCSE::ApiPointer()->Log("eyepiece: the device refused a copy of the frame");
            return false;
        }
        return true;
    }

    // The view inside the opening, redrawn through the scope's glass from a copy of the frame.
    void DrawGlass(IDirect3DDevice9* device, WeaponOverhaul::ScreenDraw& draw,
                   const WeaponOverhaul::Eyepiece::Opening& opening, IDirect3DSurface9* frame) {
        IDirect3DPixelShader9* shader = g_glassShader.Get(device);
        IDirect3DTexture9* dirt = shader != nullptr ? DirtFor(device) : nullptr;
        if (dirt == nullptr || !EnsureCopy(device, frame) ||
            FAILED(device->StretchRect(frame, nullptr, g_copy.surface, nullptr, D3DTEXF_NONE))) {
            return;
        }
        const float width = static_cast<float>(WeaponOverhaul::Frame::Width());
        const float height = static_cast<float>(WeaponOverhaul::Frame::Height());
        const float constants[12] = {
            kPincushion, kFringePerMagnification * WeaponOverhaul::Aim::Magnification(),
            kRimDarkening, kSmudgeGlow,
            width / height, opening.x, opening.y, opening.radius,
            kTint[0], kTint[1], kTint[2], 0.0f,
        };
        device->SetPixelShaderConstantF(0, constants, 3);
        device->SetTexture(0, g_copy.texture);
        device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        device->SetTexture(2, dirt);
        device->SetSamplerState(2, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(2, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        device->SetPixelShader(shader);
        draw.Quad(0.0f, 0.0f, width, height);
    }

    // The scope's reticle, each piece from the texture the engine drew it with, placed about the
    // look's centre as the opening is. Each point also carries where it is from the lens's centre,
    // which the shader keeps the reticle within.
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
            if (!Held(g_seen[i].frame)) {
                continue;
            }
            device->SetTexture(0, g_seen[i].texture);
            const float lit = piece.look == WeaponOverhaul::Scopes::Look::Lit ? 1.0f : 0.0f;
            const float look[4] = {lit, lit, lit, 1.0f - lit};
            device->SetPixelShaderConstantF(0, look, 1);
            vertices.clear();
            vertices.reserve(piece.triangles.size());
            for (const WeaponOverhaul::Scopes::Point& point : piece.triangles) {
                vertices.push_back({centreX + point.x * scale, centreY - point.y * scale, point.u,
                                    point.v, point.x - scope.lensX, scope.lensY - point.y});
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
    FollowWeapon();
    if (g_inHand == nullptr) {
        if (Aim::ScopeUp()) {
            LogStranger();
        }
        return true;
    }
    // The buffer only matters for a draw from the whole of it, which shows which it is, or while
    // the eyepiece is up.
    if (call.numVertices != g_inHand->vertices && !Held(g_drawnFrame)) {
        return true;
    }
    IDirect3DVertexBuffer9* stream = nullptr;
    UINT offset = 0;
    UINT stride = 0;
    device->GetStreamSource(0, &stream, &offset, &stride);
    const void* vertices = Borrowed(stream);
    if (call.numVertices == g_inHand->vertices) {
        g_inHandVertices = vertices;
    }
    if (vertices != g_inHandVertices) {
        return true;
    }
    // Only the sight picture draws the housing in the gun's depth pass, which comes first; the
    // weapon's draws go with the eyepiece while it is held.
    if (call.depthPass && call.primitiveCount == g_inHand->housing) {
        g_drawnFrame = Frame::Number();
    }
    if (!Held(g_drawnFrame)) {
        return true;
    }
    // A piece of the reticle is told by its draw's size, which no other draw of the weapon shares.
    const std::span<const Scopes::Piece> pieces = g_inHand->pieces;
    for (size_t i = 0; i < pieces.size() && i < kMostPieces; i++) {
        if (pieces[i].triangles.size() / 3 == call.primitiveCount) {
            IDirect3DBaseTexture9* texture = nullptr;
            device->GetTexture(0, &texture);
            g_seen[i] = {Borrowed(texture), Frame::Number()};
        }
    }
    return false;
}

void WeaponOverhaul::Eyepiece::OnComposite(IDirect3DDevice9* device) {
    const std::optional<Opening> opening = Open();
    IDirect3DPixelShader9* body = opening ? g_bodyShader.Get(device) : nullptr;
    if (body == nullptr) {
        return;
    }
    const Scope& scope = *g_inHand;
    IDirect3DTexture9* shape = ShapeFor(device, scope);
    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    const float radius = opening->radius;
    // c1 to c3: the lens on screen, the body in lens radii, and where the shape is from the lens.
    const float constants[12] = {
        width / height, opening->x, opening->y, radius,
        1.0f - (kBodyOverlap + kBodySoftness) / radius, kBodyRadius / radius,
        kMountHalfWidth / radius, kBodySoftness / radius,
        -scope.lensX, scope.lensY, shape != nullptr ? Scopes::kShapeWidth : 0.0f,
        Scopes::kShapeSpan,
    };

    IDirect3DSurface9* frame = nullptr;
    device->GetRenderTarget(0, &frame);
    ScreenDraw draw(device, 0, 4);
    // Each lays its colour over what is there by how much it covers it.
    device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);
    device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
    device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                       D3DCOLORWRITEENABLE_GREEN |
                                                       D3DCOLORWRITEENABLE_BLUE);
    DrawGlass(device, draw, *opening, frame);
    Release(frame);
    DrawReticle(device, draw, scope, *opening);

    device->SetPixelShaderConstantF(1, constants, 3);
    device->SetTexture(1, shape);
    device->SetSamplerState(1, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    device->SetSamplerState(1, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    device->SetPixelShader(body);
    draw.Quad(0.0f, 0.0f, width, height);
}

std::optional<WeaponOverhaul::Eyepiece::Opening> WeaponOverhaul::Eyepiece::Open() {
    if (!g_enabled || g_inHand == nullptr || !Held(g_drawnFrame)) {
        return std::nullopt;
    }
    const Scope& scope = *g_inHand;
    const float radius = kBodyRadius / (1.0f + scope.rim);
    const Aim::Swing swing = Aim::ScopeSwing();
    return Opening{(kTrail * swing.x + scope.lensX) * radius,
                   (kTrail * swing.y - scope.lensY) * radius, radius};
}

bool WeaponOverhaul::Eyepiece::Expected() {
    return g_enabled && g_inHand != nullptr && (Aim::ScopeUp() || Held(g_drawnFrame));
}

void WeaponOverhaul::Eyepiece::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::Eyepiece::ReleaseDeviceObjects() {
    g_reticleShader.Release();
    g_bodyShader.Release();
    g_glassShader.Release();
    Release(g_shape);
    g_shapeOwner = nullptr;
    g_shapeOf = nullptr;
    Release(g_copy);
    g_copyOwner = nullptr;
    g_copyRefused = false;
    Release(g_dirt);
    g_dirtOwner = nullptr;
    std::fill(std::begin(g_seen), std::end(g_seen), Seen{});
}
