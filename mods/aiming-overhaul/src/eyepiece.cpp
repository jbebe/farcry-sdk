// The engine's scope is dropped once the weapon in hand says which scope it is; the composite then
// redraws the view inside the opening through the scope's glass, lays the scope's reticle over it
// and the body over both.
#include "eyepiece.h"

#include "engine/aim.h"
#include "engine/com.h"
#include "engine/image_file.h"
#include "engine/frame.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "engine/weapon_mesh.h"
#include "fcse_api.h"
#include "scopes.h"

#include "eyepiece_body_ps.h"
#include "eyepiece_reticle_ps.h"
#include "glass_ps.h"

#include <atomic>
#include <cstring>

namespace {
    using AimingOverhaul::Scopes::Scope;

    // The body's outer edge at a scope's full size, the mount's half-width and the body's softness,
    // as shares of the screen's height.
    constexpr float kBodyRadius = 0.4f;
    constexpr float kMountHalfWidth = 0.105f;
    constexpr float kBodySoftness = 0.0125f;
    // How far inside the opening the body is wholly black, so that its soft edge lies over the
    // shadow and the reticle rather than leaving a light seam between them and it.
    constexpr float kBodyOverlap = 0.005f;
    // How far the scope trails the look's turn, as a share of the shadow's swing.
    constexpr float kTrail = 0.1f;
    // How much bigger the scope is drawn at the height of a shot's kick, as it comes back toward
    // the eye.
    constexpr float kKickGrowth = 0.06f;
    // The eyepiece outlasts the sight picture by a frame: the frame it goes in can still be drawn
    // at the scope's field of view.
    constexpr uint32_t kHeldFrames = 1;

    // The glass, at the lens's rim: how far the view is pulled in, how far red and blue part from
    // green for each time the scope magnifies, and how much the view darkens; and what the coating
    // lets through, a little less light with a faint green.
    constexpr float kPincushion = 0.12f;
    constexpr float kFringePerMagnification = 0.004f;
    constexpr float kRimDarkening = 0.5f;
    constexpr float kTint[3] = {0.92f, 0.96f, 0.93f};

    // An illuminated reticle: how much light it spreads around its strokes, and how far their
    // insides burn toward a hotter shade of their colour.
    constexpr float kHalo = 0.35f;
    constexpr float kHeat = 1.0f;

    AimingOverhaul::PixelShader g_reticleShader{"eyepiece reticle", g_eyepieceReticlePixelShader};
    AimingOverhaul::PixelShader g_bodyShader{"eyepiece body", g_eyepieceBodyPixelShader};
    AimingOverhaul::PixelShader g_glassShader{"eyepiece glass", g_glassPixelShader};

    // A copy of the finished frame for the glass to look through, on the device that made it;
    // refused until a reset if the device would not make it.
    IDirect3DDevice9* g_copyOwner = nullptr;
    AimingOverhaul::Target g_copy;
    D3DSURFACE_DESC g_copyDesc = {};
    bool g_copyRefused = false;

    std::atomic<bool> g_enabled{true};

    // The scope of the weapon in hand as of its last draw, and the change of weapon a weapon whose
    // scope the plugin does not draw was last named for.
    const Scope* g_inHand = nullptr;
    uint32_t g_strangerFor = AimingOverhaul::Frame::kNever;

    // The frame the weapon in hand last drew its scope's sight picture in, which the render thread
    // can draw a frame after the game has put it away.
    uint32_t g_drawnFrame = AimingOverhaul::Frame::kNever;

    // The texture made from this reticle's image, on the device that owns it.
    IDirect3DDevice9* g_reticleOwner = nullptr;
    char g_reticleOf[sizeof(AimingOverhaul::Scopes::Scope::reticle)] = {};
    IDirect3DTexture9* g_reticle = nullptr;

    // The distance field, the shape it was made from, and the device that owns it.
    IDirect3DDevice9* g_shapeOwner = nullptr;
    const BYTE* g_shapeOf = nullptr;
    IDirect3DTexture9* g_shape = nullptr;

    bool Held(uint32_t frame) {
        return AimingOverhaul::Frame::Within(frame, kHeldFrames);
    }

    // Names a weapon whose scope is drawn as the game draws it, for want of scope data or of a
    // SCOPE_HI part found in its mesh, once each time it is taken in hand.
    void LogStranger() {
        const uint32_t changes = AimingOverhaul::Aim::WeaponChanges();
        if (g_strangerFor != changes) {
            g_strangerFor = changes;
            FCSE::Logf("eyepiece: the scope on \"%s\" is drawn as the game draws it: it has no "
                       "scope data, or no SCOPE_HI part was found in its mesh",
                       AimingOverhaul::Aim::WeaponName());
        }
    }

    // The scope's own shape, made the first time it is asked for; null for a plain ring.
    IDirect3DTexture9* ShapeFor(IDirect3DDevice9* device, const Scope& scope) {
        if (scope.shape.empty()) {
            return nullptr;
        }
        if (g_shapeOwner == device && g_shapeOf == scope.shape.data()) {
            return g_shape;
        }
        AimingOverhaul::Release(g_shape);
        g_shapeOwner = device;
        g_shapeOf = scope.shape.data();
        constexpr UINT size = AimingOverhaul::Scopes::kShapeSize;
        D3DLOCKED_RECT locked = {};
        if (FAILED(device->CreateTexture(size, size, 1, 0, D3DFMT_L8, D3DPOOL_MANAGED, &g_shape,
                                         nullptr)) ||
            FAILED(g_shape->LockRect(0, &locked, nullptr, 0))) {
            AimingOverhaul::Release(g_shape);
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

    // The scope's reticle, made the first time it is asked for after another's.
    IDirect3DTexture9* ReticleFor(IDirect3DDevice9* device, const Scope& scope) {
        if (g_reticleOwner == device && std::strcmp(g_reticleOf, scope.reticle) == 0) {
            return g_reticle;
        }
        AimingOverhaul::Release(g_reticle);
        g_reticleOwner = device;
        std::strcpy(g_reticleOf, scope.reticle);
        const ULONGLONG start = GetTickCount64();
        g_reticle = AimingOverhaul::ImageFile::Texture(device, scope.reticle);
        if (g_reticle != nullptr) {
            FCSE::Logf("eyepiece: %s made in %llu ms", scope.reticle, GetTickCount64() - start);
        }
        return g_reticle;
    }

    bool EnsureCopy(IDirect3DDevice9* device, IDirect3DSurface9* frame) {
        D3DSURFACE_DESC desc = {};
        frame->GetDesc(&desc);
        if (g_copyOwner == device && g_copy.texture != nullptr && g_copyDesc.Width == desc.Width &&
            g_copyDesc.Height == desc.Height && g_copyDesc.Format == desc.Format) {
            return true;
        }
        AimingOverhaul::Release(g_copy);
        if (g_copyRefused) {
            return false;
        }
        g_copyOwner = device;
        g_copyDesc = desc;
        if (FAILED(AimingOverhaul::CreateTarget(device, desc.Width, desc.Height, desc.Format,
                                                g_copy))) {
            AimingOverhaul::Release(g_copy);
            g_copyRefused = true;
            FCSE::ApiPointer()->Log("eyepiece: the device refused a copy of the frame");
            return false;
        }
        return true;
    }

    // The view inside the opening, redrawn through the scope's glass from a copy of the frame.
    void DrawGlass(IDirect3DDevice9* device, AimingOverhaul::ScreenDraw& draw,
                   const AimingOverhaul::Eyepiece::Opening& opening, IDirect3DSurface9* frame) {
        IDirect3DPixelShader9* shader = g_glassShader.Get(device);
        if (shader == nullptr || !EnsureCopy(device, frame) ||
            FAILED(device->StretchRect(frame, nullptr, g_copy.surface, nullptr, D3DTEXF_NONE))) {
            return;
        }
        const float width = static_cast<float>(AimingOverhaul::Frame::Width());
        const float height = static_cast<float>(AimingOverhaul::Frame::Height());
        const float constants[12] = {
            kPincushion, kFringePerMagnification * AimingOverhaul::Aim::Magnification(),
            kRimDarkening, 0.0f,
            width / height, opening.x, opening.y, opening.radius,
            kTint[0], kTint[1], kTint[2], 0.0f,
        };
        device->SetPixelShaderConstantF(0, constants, 3);
        device->SetTexture(0, g_copy.texture);
        draw.Linear(0);
        device->SetPixelShader(shader);
        draw.Quad(0.0f, 0.0f, width, height);
    }

    // The scope's reticle, its image's centre on the look's and its edge a lens radius out. Each
    // corner also carries where it is from the lens's centre, which the shader keeps the reticle
    // within.
    void DrawReticle(IDirect3DDevice9* device, AimingOverhaul::ScreenDraw& draw, const Scope& scope,
                     const AimingOverhaul::Eyepiece::Opening& opening) {
        IDirect3DPixelShader9* shader = g_reticleShader.Get(device);
        IDirect3DTexture9* reticle = shader != nullptr ? ReticleFor(device, scope) : nullptr;
        if (reticle == nullptr) {
            return;
        }
        const float width = static_cast<float>(AimingOverhaul::Frame::Width());
        const float height = static_cast<float>(AimingOverhaul::Frame::Height());
        const float scale = opening.radius * height;
        const float centreX = width / 2.0f + opening.x * height - scope.lensX * scale;
        const float centreY = height / 2.0f + opening.y * height + scope.lensY * scale;

        // The image's corners in lens radii from the look's centre, x right and y up.
        constexpr float kCorners[6][2] = {{-1, 1}, {1, 1}, {1, -1}, {1, -1}, {-1, -1}, {-1, 1}};
        AimingOverhaul::ScreenDraw::Vertex vertices[6];
        for (size_t i = 0; i < 6; i++) {
            const float x = kCorners[i][0];
            const float y = kCorners[i][1];
            vertices[i] = {centreX + x * scale, centreY - y * scale, (x + 1.0f) / 2.0f,
                           (1.0f - y) / 2.0f, x - scope.lensX, scope.lensY - y};
        }
        const float lit = scope.lit ? 1.0f : 0.0f;
        const float light[4] = {lit * kHalo, lit * kHeat, 0.0f, 0.0f};
        device->SetPixelShaderConstantF(0, light, 1);
        draw.Linear(0);
        device->SetSamplerState(0, D3DSAMP_MIPFILTER, D3DTEXF_LINEAR);
        device->SetTexture(0, reticle);
        device->SetPixelShader(shader);
        draw.Triangles(vertices, 6);
    }
}

bool AimingOverhaul::Eyepiece::BeforeGunDraw(IDirect3DDevice9* device,
                                             const WeaponDraws::Call& call) {
    if (!g_enabled) {
        return true;
    }
    g_inHand = Scopes::InHand();
    if (g_inHand == nullptr) {
        if (Aim::ScopeUp()) {
            LogStranger();
        }
        return true;
    }
    // Only the sight picture draws the scope's part, in the gun's depth pass first; the weapon's
    // draws go with the eyepiece while it is held.
    const bool sightPicture = call.depthPass && g_inHand->part.Draws(call.startIndex);
    if (!sightPicture && !Held(g_drawnFrame)) {
        return true;
    }
    IDirect3DVertexBuffer9* stream = nullptr;
    UINT offset = 0;
    UINT stride = 0;
    device->GetStreamSource(0, &stream, &offset, &stride);
    if (Borrowed(stream) != WeaponMesh::VertexBuffer(g_inHand->part.vertices)) {
        return true;
    }
    if (sightPicture) {
        g_drawnFrame = Frame::Number();
    }
    return !Held(g_drawnFrame);
}

void AimingOverhaul::Eyepiece::OnComposite(IDirect3DDevice9* device) {
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
        1.0f - (kBodyOverlap + kBodySoftness) / radius, opening->outer / radius,
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
    draw.KeepAlpha();
    DrawGlass(device, draw, *opening, frame);
    Release(frame);
    // The reticle's colour is already multiplied by its cover, and a lit one adds light past it.
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ONE);
    DrawReticle(device, draw, scope, *opening);
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_SRCALPHA);

    device->SetPixelShaderConstantF(1, constants, 3);
    device->SetTexture(1, shape);
    draw.Linear(1);
    device->SetPixelShader(body);
    draw.Quad(0.0f, 0.0f, width, height);
}

std::optional<AimingOverhaul::Eyepiece::Opening> AimingOverhaul::Eyepiece::Open() {
    if (!g_enabled || g_inHand == nullptr || !Held(g_drawnFrame)) {
        return std::nullopt;
    }
    const Scope& scope = *g_inHand;
    const float outer = kBodyRadius * scope.size * (1.0f + kKickGrowth * Aim::ScopeKick());
    const float radius = outer / (1.0f + scope.rim);
    const Aim::Swing swing = Aim::ScopeSwing();
    return Opening{(kTrail * swing.x + scope.lensX) * radius,
                   (kTrail * swing.y - scope.lensY) * radius, radius, outer};
}

bool AimingOverhaul::Eyepiece::Expected() {
    return g_enabled && g_inHand != nullptr && (Aim::ScopeUp() || Held(g_drawnFrame));
}

void AimingOverhaul::Eyepiece::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void AimingOverhaul::Eyepiece::ReleaseDeviceObjects() {
    g_reticleShader.Release();
    g_bodyShader.Release();
    g_glassShader.Release();
    Release(g_shape);
    g_shapeOwner = nullptr;
    g_shapeOf = nullptr;
    Release(g_reticle);
    g_reticleOwner = nullptr;
    g_reticleOf[0] = '\0';
    Release(g_copy);
    g_copyOwner = nullptr;
    g_copyRefused = false;
}
