// A scope is measured from its depth, read back each frame once it has settled until two readings
// agree, and what was found is kept for that scope. The engine's housing is not drawn: the scope's
// body is laid over the finished frame instead, and the reticle, drawn into a mask of its own, with
// it.
#include "eyepiece.h"

#include "engine/aim.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "scope_lens.h"

#include "eyepiece_body_ps.h"
#include "eyepiece_depth_ps.h"
#include "eyepiece_reticle_ps.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <iterator>
#include <numbers>
#include <optional>
#include <vector>

namespace {
    using WeaponOverhaul::WeaponDraws::Projection;

    // How much of the scope is kept, in metres past its nearest point.
    constexpr float kKept = 0.01f;

    // The rays cast to the openings' edges, and the fewest edge points a circle is fitted to.
    constexpr int kRays = 72;
    constexpr size_t kFewestPoints = 8;
    // What a scope's openings can be: how far off the screen's centre, in screen heights, and how
    // much wider the eyepiece than the engine's.
    constexpr float kFurthestOff = 0.15f;
    constexpr float kMostGrowth = 4.0f;
    // Two measurements in a row agree, and the scope has settled, within these: view-space slope,
    // metres, and a share of the growth. Frames a scope-up is measured in before it is given up on.
    constexpr float kSlopeAgreement = 0.002f;
    constexpr float kDistanceAgreement = 0.002f;
    constexpr float kGrowthAgreement = 0.02f;
    constexpr int kAttempts = 60;
    // Scopes remembered.
    constexpr size_t kScopes = 8;

    // The scope's body as it is drawn for every scope: its outer edge's radius as a share of the
    // screen's height, its rim and the mount below it as shares of the opening's radius, and its
    // softness, also a share of the screen's height.
    constexpr float kBodyRadius = 0.4f;
    constexpr float kRim = 0.15f;
    constexpr float kMountHalfWidth = 0.6f;
    constexpr float kBodySoftness = 0.025f;
    constexpr float kOpeningRadius = kBodyRadius / (1.0f + kRim);

    // The reticle's softness, a radius as a share of the screen's height, and how much its blurred
    // coverage is strengthened so thin lines stay black.
    constexpr float kSoftness = 0.0018f;
    constexpr float kInk = 2.0f;

    // Where a mesh's vertex shader finds the camera's rotation and projection, one register for
    // each of clip space's x, y, z and w.
    constexpr UINT kViewRotProjectionRegister = 0;

    // What a reticle draw is given to be drawn into the mask: nought wherever it lands.
    constexpr DWORD kTargets = 4;
    constexpr D3DRENDERSTATETYPE kMaskStates[] = {
        D3DRS_ZENABLE,          D3DRS_STENCILENABLE, D3DRS_SCISSORTESTENABLE,
        D3DRS_ALPHABLENDENABLE, D3DRS_SEPARATEALPHABLENDENABLE,
        D3DRS_SRCBLEND,         D3DRS_DESTBLEND,     D3DRS_BLENDOP,
        D3DRS_COLORWRITEENABLE, D3DRS_SRGBWRITEENABLE,
    };
    constexpr DWORD kAllColour = D3DCOLORWRITEENABLE_RED | D3DCOLORWRITEENABLE_GREEN |
                                 D3DCOLORWRITEENABLE_BLUE | D3DCOLORWRITEENABLE_ALPHA;

    WeaponOverhaul::PixelShader g_depthShader{"eyepiece depth", g_eyepieceDepthPixelShader};
    WeaponOverhaul::PixelShader g_reticleShader{"eyepiece reticle", g_eyepieceReticlePixelShader};
    WeaponOverhaul::PixelShader g_bodyShader{"eyepiece body", g_eyepieceBodyPixelShader};

    std::atomic<bool> g_enabled{true};

    struct Point {
        float x;
        float y;
    };

    struct Circle {
        float x;
        float y;
        float radius;
    };

    // A point of the scope, as a view-space slope, x right and y up, and how far away it is in
    // metres.
    struct Spot {
        float x;
        float y;
        float distance;
    };

    struct Scope {
        // The scope's vertex buffer and the reticle's texture, compared, never dereferenced.
        const void* vertices;
        const void* reticle;
        // The distance the scope is cut at, in metres, and how much the reticle grows.
        float keptTo;
        float growth;
        // The reticle's centre at the scope's far end, and the opening's, with its radius as a slope.
        Spot farEnd;
        Spot opening;
        float radius;
    };

    Scope g_scopes[kScopes] = {};
    size_t g_nextScope = 0;

    // One scope-up: which it is, the scope's draws as seen, the measurements so far, and the scope
    // it is cut as, none until one is known.
    struct ScopeUp {
        uint32_t number;
        const void* vertices;
        const void* reticle;
        int attempts;
        bool settled;
        std::optional<Scope> last;
        Scope* scope;
    };
    ScopeUp g_up = {};

    // The reticle's mask, one where it was not drawn, and the frame it was last drawn in.
    IDirect3DDevice9* g_maskOwner = nullptr;
    WeaponOverhaul::Target g_mask;
    UINT g_maskWidth = 0;
    UINT g_maskHeight = 0;
    uint32_t g_maskFrame = WeaponOverhaul::Frame::kNever;
    // Set once the device refuses the mask; cleared on reset.
    bool g_maskRefused = false;

    // What the reticle's draw under way changed, and what was there before.
    bool g_intoMask = false;
    float g_savedRows[8] = {};
    IDirect3DSurface9* g_savedTargets[kTargets] = {};
    IDirect3DSurface9* g_savedDepth = nullptr;
    D3DVIEWPORT9 g_savedViewport = {};
    DWORD g_savedMaskStates[std::size(kMaskStates)] = {};

    double Determinant(double a, double b, double c, double d, double e, double f, double g,
                       double h, double i) {
        return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }

    // The circle x^2 + y^2 + Dx + Ey + F = 0 nearest the points by least squares.
    bool Fit(const std::vector<Point>& points, Circle& circle) {
        if (points.size() < kFewestPoints) {
            return false;
        }
        double xx = 0.0, xy = 0.0, yy = 0.0, x = 0.0, y = 0.0, xz = 0.0, yz = 0.0, z = 0.0;
        for (const Point& point : points) {
            const double px = point.x;
            const double py = point.y;
            const double squared = px * px + py * py;
            xx += px * px;
            xy += px * py;
            yy += py * py;
            x += px;
            y += py;
            xz += px * squared;
            yz += py * squared;
            z += squared;
        }
        const double n = static_cast<double>(points.size());
        const double whole = Determinant(xx, xy, x, xy, yy, y, x, y, n);
        if (std::abs(whole) < 1e-9) {
            return false;
        }
        const double d = Determinant(-xz, xy, x, -yz, yy, y, -z, y, n) / whole;
        const double e = Determinant(xx, -xz, x, xy, -yz, y, x, -z, n) / whole;
        const double f = Determinant(xx, xy, -xz, xy, yy, -yz, x, y, -z) / whole;
        const double cx = -d / 2.0;
        const double cy = -e / 2.0;
        const double squaredRadius = cx * cx + cy * cy - f;
        if (squaredRadius <= 0.0) {
            return false;
        }
        circle = {static_cast<float>(cx), static_cast<float>(cy),
                  static_cast<float>(std::sqrt(squaredRadius))};
        return true;
    }

    // From the depth read back: where the cut goes, and the two openings.
    bool Measure(const WeaponOverhaul::WeaponDraws::Depth& depth, const BYTE* bits, INT pitch,
                 Scope& scope) {
        const Projection& projection = depth.projection;
        const auto row = [&](UINT y) { return reinterpret_cast<const float*>(bits + y * pitch); };
        float nearest = 1.0f;
        for (UINT y = 0; y < depth.height; y++) {
            const float* stored = row(y);
            nearest = (std::min)(nearest, *std::min_element(stored, stored + depth.width));
        }
        if (nearest >= 1.0f) {
            return false;
        }
        scope.keptTo = projection.Metres(nearest) + kKept;
        const float cut = projection.Stored(scope.keptTo);

        // Along each ray the scope's far end is met first, then the part that is kept.
        const float centreX = depth.width / 2.0f;
        const float centreY = depth.height / 2.0f;
        std::vector<Point> engine;
        std::vector<Point> eyepiece;
        engine.reserve(kRays);
        eyepiece.reserve(kRays);
        float farDistances = 0.0f;
        float openingDistances = 0.0f;
        for (int i = 0; i < kRays; i++) {
            const float angle = (i + 0.5f) * 2.0f * std::numbers::pi_v<float> / kRays;
            const float dx = std::cos(angle);
            const float dy = std::sin(angle);
            bool met = false;
            for (float r = 1.0f;; r += 1.0f) {
                const float x = centreX + dx * r;
                const float y = centreY + dy * r;
                if (x < 0.0f || y < 0.0f || x >= depth.width || y >= depth.height) {
                    break;
                }
                const float stored = row(static_cast<UINT>(y))[static_cast<UINT>(x)];
                if (!met && stored < 1.0f) {
                    met = true;
                    engine.push_back({x - centreX, y - centreY});
                    farDistances += projection.Metres(stored);
                }
                if (stored <= cut) {
                    eyepiece.push_back({x - centreX, y - centreY});
                    openingDistances += projection.Metres(stored);
                    break;
                }
            }
        }
        Circle before = {};
        Circle after = {};
        const float off = kFurthestOff * depth.height;
        if (!Fit(engine, before) || !Fit(eyepiece, after) ||
            std::hypot(before.x, before.y) > off || std::hypot(after.x, after.y) > off ||
            after.radius <= before.radius || after.radius > kMostGrowth * before.radius) {
            return false;
        }

        const auto spot = [&](const Circle& circle, float distance) {
            return Spot{circle.x / centreX / projection.horizontalScale,
                        -circle.y / centreY / projection.verticalScale, distance};
        };
        scope.farEnd = spot(before, farDistances / engine.size());
        scope.opening = spot(after, openingDistances / eyepiece.size());
        scope.radius = after.radius / centreY / projection.verticalScale;
        scope.growth = after.radius / before.radius;
        return scope.farEnd.distance > scope.opening.distance;
    }

    // Reads the depth back through a float target, which the device can copy to memory.
    bool ReadBack(const WeaponOverhaul::Frame::Pass& pass,
                  const WeaponOverhaul::WeaponDraws::Depth& depth, Scope& scope) {
        IDirect3DDevice9* device = pass.device;
        IDirect3DPixelShader9* shader = g_depthShader.Get(device);
        WeaponOverhaul::Target copy;
        IDirect3DSurface9* memory = nullptr;
        if (shader == nullptr ||
            FAILED(WeaponOverhaul::CreateTarget(device, depth.width, depth.height, D3DFMT_R32F,
                                                copy)) ||
            FAILED(device->CreateOffscreenPlainSurface(depth.width, depth.height, D3DFMT_R32F,
                                                       D3DPOOL_SYSTEMMEM, &memory, nullptr))) {
            FCSE::ApiPointer()->Log("eyepiece: the device refused the depth's readback");
            WeaponOverhaul::Release(copy);
            return false;
        }

        {
            WeaponOverhaul::ScreenDraw draw(device, 0, 0);
            device->SetRenderTarget(0, copy.surface);
            device->SetDepthStencilSurface(nullptr);
            device->SetTexture(3, depth.texture);
            device->SetPixelShader(shader);
            draw.Quad(0.0f, 0.0f, static_cast<float>(depth.width),
                      static_cast<float>(depth.height));
        }

        bool measured = false;
        D3DLOCKED_RECT locked = {};
        if (SUCCEEDED(device->GetRenderTargetData(copy.surface, memory)) &&
            SUCCEEDED(memory->LockRect(&locked, nullptr, D3DLOCK_READONLY))) {
            measured = Measure(depth, static_cast<const BYTE*>(locked.pBits), locked.Pitch, scope);
            memory->UnlockRect();
        } else {
            FCSE::ApiPointer()->Log("eyepiece: the depth could not be read back");
        }
        WeaponOverhaul::Release(memory);
        WeaponOverhaul::Release(copy);
        return measured;
    }

    bool Agree(const Scope& a, const Scope& b) {
        return std::abs(a.keptTo - b.keptTo) < kDistanceAgreement &&
               std::abs(a.opening.x - b.opening.x) < kSlopeAgreement &&
               std::abs(a.opening.y - b.opening.y) < kSlopeAgreement &&
               std::abs(a.radius - b.radius) < kSlopeAgreement &&
               std::abs(a.growth - b.growth) < kGrowthAgreement * a.growth;
    }

    Scope* Find(const void* vertices) {
        for (Scope& scope : g_scopes) {
            if (vertices != nullptr && scope.vertices == vertices) {
                return &scope;
            }
        }
        return nullptr;
    }

    // Keeps a settled measurement under the scope's draws, in place of what was kept for it.
    void Keep(Scope scope) {
        scope.vertices = g_up.vertices;
        scope.reticle = g_up.reticle;
        g_up.scope = Find(g_up.vertices);
        if (g_up.scope == nullptr) {
            g_up.scope = &g_scopes[g_nextScope];
            g_nextScope = (g_nextScope + 1) % kScopes;
        }
        *g_up.scope = scope;
        FCSE::Logf("eyepiece: measured after %d frames: cut at %.3f m; the opening is %.3f m away "
                   "and grows x%.2f to a slope of %.3f; the reticle is %.3f m away",
                   g_up.attempts, scope.keptTo, scope.opening.distance, scope.growth, scope.radius,
                   scope.farEnd.distance);
    }

    // Starts over each time a scope comes up.
    void Follow() {
        const uint32_t number = WeaponOverhaul::Aim::ScopeUps();
        if (g_up.number != number) {
            g_up = {};
            g_up.number = number;
        }
    }

    // Scales the reticle's draw in clip space about its own centre, to the opening drawn as it was
    // to the engine's lens, and moves that centre onto the opening's.
    void Grow(IDirect3DDevice9* device, const Projection& projection) {
        const Scope& scope = *g_up.scope;
        const float lens = scope.radius / scope.growth;
        const float growth = 2.0f * kOpeningRadius / projection.verticalScale / lens;
        const float farX = scope.farEnd.x * projection.horizontalScale;
        const float farY = scope.farEnd.y * projection.verticalScale;
        const float x = scope.opening.x * projection.horizontalScale;
        const float y = scope.opening.y * projection.verticalScale;
        float camera[16] = {};
        device->GetVertexShaderConstantF(kViewRotProjectionRegister, camera, 4);
        float rows[8] = {};
        for (int i = 0; i < 4; i++) {
            rows[i] = growth * camera[i] + (x - growth * farX) * camera[12 + i];
            rows[4 + i] = growth * camera[4 + i] + (y - growth * farY) * camera[12 + i];
        }
        std::copy_n(camera, std::size(g_savedRows), g_savedRows);
        device->SetVertexShaderConstantF(kViewRotProjectionRegister, rows, 2);
    }

    bool EnsureMask(IDirect3DDevice9* device) {
        const UINT width = WeaponOverhaul::Frame::Width();
        const UINT height = WeaponOverhaul::Frame::Height();
        if (g_maskOwner == device && g_mask.texture != nullptr && g_maskWidth == width &&
            g_maskHeight == height) {
            return true;
        }
        WeaponOverhaul::Release(g_mask);
        if (g_maskRefused) {
            return false;
        }
        g_maskOwner = device;
        g_maskWidth = width;
        g_maskHeight = height;
        if (FAILED(WeaponOverhaul::CreateTarget(device, width, height, D3DFMT_A8R8G8B8, g_mask))) {
            WeaponOverhaul::Release(g_mask);
            g_maskRefused = true;
            FCSE::Logf("eyepiece: the device refused a %ux%u reticle mask", width, height);
            return false;
        }
        return true;
    }

    // Sends the draw under way into the reticle's mask, blended by `destBlend` so it leaves nought
    // where it lands, and clears the mask first each frame.
    bool IntoMask(IDirect3DDevice9* device, DWORD destBlend) {
        if (!EnsureMask(device)) {
            return false;
        }
        for (DWORD i = 0; i < kTargets; i++) {
            device->GetRenderTarget(i, &g_savedTargets[i]);
        }
        device->GetDepthStencilSurface(&g_savedDepth);
        device->GetViewport(&g_savedViewport);
        for (size_t i = 0; i < std::size(kMaskStates); i++) {
            device->GetRenderState(kMaskStates[i], &g_savedMaskStates[i]);
        }

        device->SetRenderTarget(0, g_mask.surface);
        for (DWORD i = 1; i < kTargets; i++) {
            if (g_savedTargets[i] != nullptr) {
                device->SetRenderTarget(i, nullptr);
            }
        }
        device->SetDepthStencilSurface(nullptr);
        const uint32_t frame = WeaponOverhaul::Frame::Number();
        if (g_maskFrame != frame) {
            g_maskFrame = frame;
            device->Clear(0, nullptr, D3DCLEAR_TARGET, 0xFFFFFFFF, 1.0f, 0);
        }
        const DWORD states[] = {D3DZB_FALSE, FALSE, FALSE, TRUE, FALSE, D3DBLEND_ZERO,
                                destBlend, D3DBLENDOP_ADD, kAllColour, FALSE};
        for (size_t i = 0; i < std::size(kMaskStates); i++) {
            device->SetRenderState(kMaskStates[i], states[i]);
        }
        g_intoMask = true;
        return true;
    }

    void OutOfMask(IDirect3DDevice9* device) {
        for (DWORD i = 0; i < kTargets; i++) {
            if (i == 0 || g_savedTargets[i] != nullptr) {
                device->SetRenderTarget(i, g_savedTargets[i]);
            }
            WeaponOverhaul::Release(g_savedTargets[i]);
        }
        device->SetDepthStencilSurface(g_savedDepth);
        WeaponOverhaul::Release(g_savedDepth);
        device->SetViewport(&g_savedViewport);
        for (size_t i = 0; i < std::size(kMaskStates); i++) {
            device->SetRenderState(kMaskStates[i], g_savedMaskStates[i]);
        }
        g_intoMask = false;
    }

    // A COM pointer's identity, without the reference the getter added.
    template <typename T>
    const void* Identity(T* object) {
        if (object != nullptr) {
            object->Release();
        }
        return object;
    }

    const void* BoundTexture(IDirect3DDevice9* device) {
        IDirect3DBaseTexture9* texture = nullptr;
        device->GetTexture(0, &texture);
        return Identity(texture);
    }
}

void WeaponOverhaul::Eyepiece::OnDepthPass(const Frame::Pass& pass,
                                           const WeaponDraws::Depth& depth) {
    if (!g_enabled || !Aim::ScopeUp()) {
        return;
    }
    Follow();
    // Measured once the eye has settled into the scope.
    if (g_up.settled || g_up.attempts >= kAttempts || Aim::Scoped() < 1.0f ||
        g_up.vertices == nullptr) {
        return;
    }
    g_up.attempts++;
    Scope scope = {};
    const bool measured = ReadBack(pass, depth, scope);
    g_up.settled = measured && g_up.last && Agree(*g_up.last, scope);
    g_up.last = measured ? std::optional<Scope>(scope) : std::nullopt;
    if (g_up.settled) {
        Keep(scope);
    } else if (g_up.attempts == kAttempts) {
        FCSE::Logf("eyepiece: the scope did not settle in %d frames, so it is drawn whole",
                   kAttempts);
    }
}

bool WeaponOverhaul::Eyepiece::BeforeGunDraw(IDirect3DDevice9* device,
                                             const WeaponDraws::Projection& projection,
                                             bool depthPass) {
    if (!g_enabled || !Aim::ScopeUp()) {
        return true;
    }
    Follow();
    IDirect3DVertexBuffer9* vertexBuffer = nullptr;
    UINT offset = 0;
    UINT stride = 0;
    device->GetStreamSource(0, &vertexBuffer, &offset, &stride);
    const void* vertices = Identity(vertexBuffer);
    DWORD alphaTest = FALSE;
    device->GetRenderState(D3DRS_ALPHATESTENABLE, &alphaTest);

    // The reticle's is the scope's only alpha-tested draw.
    const void* texture = alphaTest ? BoundTexture(device) : nullptr;
    if (alphaTest) {
        g_up.vertices = vertices;
        g_up.reticle = texture;
    }
    if (g_up.scope == nullptr) {
        g_up.scope = Find(vertices);
    }
    if (g_up.scope == nullptr || vertices != g_up.scope->vertices) {
        return true;
    }
    if (alphaTest) {
        g_up.scope->reticle = texture;
    } else {
        // The housing is laid over the finished frame instead. Beside the reticle some scopes draw
        // markings blended, from the reticle's texture.
        texture = BoundTexture(device);
        if (texture == nullptr || texture != g_up.scope->reticle) {
            return false;
        }
    }
    // The alpha-tested reticle is masked from the gun's depth pass, where its texels are tested
    // the same; what is blended is masked by its own alpha.
    if (alphaTest && !depthPass && !g_maskRefused) {
        return false;
    }
    if (!IntoMask(device, alphaTest ? D3DBLEND_ZERO : D3DBLEND_INVSRCALPHA)) {
        return true;
    }
    Grow(device, projection);
    return true;
}

void WeaponOverhaul::Eyepiece::AfterGunDraw(IDirect3DDevice9* device) {
    if (g_intoMask) {
        device->SetVertexShaderConstantF(kViewRotProjectionRegister, g_savedRows, 2);
        OutOfMask(device);
    }
}

void WeaponOverhaul::Eyepiece::OnComposite(IDirect3DDevice9* device) {
    IDirect3DTexture9* lens = ScopeLens::Found();
    IDirect3DPixelShader9* body = Active() && lens != nullptr ? g_bodyShader.Get(device) : nullptr;
    if (body == nullptr) {
        return;
    }
    const float width = static_cast<float>(Frame::Width());
    const float height = static_cast<float>(Frame::Height());
    const float constants[12] = {
        1.0f / width, 1.0f / height, kSoftness * height, width / height,
        kInk, ScopeLens::kSmallest, 0.0f, 0.0f,
        1.0f + kRim, kMountHalfWidth, kBodySoftness, 0.0f,
    };

    ScreenDraw draw(device, 0, 3);
    device->SetPixelShaderConstantF(0, constants, 3);
    device->SetTexture(5, lens);
    // Both darken what is there by how much they cover it.
    device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
    device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
    device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                       D3DCOLORWRITEENABLE_GREEN |
                                                       D3DCOLORWRITEENABLE_BLUE);
    device->SetPixelShader(body);
    draw.Quad(0.0f, 0.0f, width, height);

    IDirect3DPixelShader9* reticle =
        g_maskFrame == Frame::Number() ? g_reticleShader.Get(device) : nullptr;
    if (reticle == nullptr) {
        return;
    }
    device->SetTexture(0, g_mask.texture);
    device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    device->SetPixelShader(reticle);
    draw.Quad(0.0f, 0.0f, width, height);
}

float WeaponOverhaul::Eyepiece::OpeningRadius() {
    return Active() ? kOpeningRadius : 0.0f;
}

float WeaponOverhaul::Eyepiece::Hole(const WeaponDraws::Projection& projection) {
    return Active() ? projection.Stored(g_up.scope->keptTo) : 1.0f;
}

bool WeaponOverhaul::Eyepiece::Active() {
    return g_enabled && Aim::ScopeUp() && g_up.number == Aim::ScopeUps() && g_up.scope != nullptr;
}

void WeaponOverhaul::Eyepiece::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::Eyepiece::ReleaseDeviceObjects() {
    g_depthShader.Release();
    g_reticleShader.Release();
    g_bodyShader.Release();
    WeaponOverhaul::Release(g_mask);
    g_maskOwner = nullptr;
    g_maskRefused = false;
    g_maskFrame = Frame::kNever;
    std::fill(std::begin(g_scopes), std::end(g_scopes), Scope{});
    g_up.scope = nullptr;
}
