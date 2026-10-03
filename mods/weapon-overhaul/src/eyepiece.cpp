// The eyepiece. Once a scope has come up and settled, its depth is read back: the nearest point is
// the tube's eye end, and two circles are fitted along rays from the screen's centre, the opening as
// the engine draws it and the opening once the tube is cut. What is found is kept per scope, so the
// next time that scope comes up it is cut from the first frame. The reticle sits at the scope's far
// end, so it swings with the shadow's clear circle, and only shows through the opening.
#include "eyepiece.h"

#include "engine/aim.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "scope_shadow.h"

#include "eyepiece_depth_ps.h"
#include "eyepiece_mask_ps.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <iterator>
#include <numbers>
#include <vector>

namespace {
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

    // The stencil bit the opening is marked with, clear of the engine's top bit and Sky Overhaul's
    // 0x40, and how far past the opening's edge it reaches, in its radii; then a reach that covers
    // the screen.
    constexpr DWORD kMark = 0x20;
    constexpr float kMarkReach = 1.05f;
    constexpr float kEverywhere = 1.0e4f;

    // Where a mesh's vertex shader finds the camera's rotation and projection, one register for
    // each of clip space's x, y, z and w.
    constexpr UINT kViewRotProjectionRegister = 0;
    constexpr uint32_t kNone = 0xFFFFFFFFu;

    // What the reticle's draw is given, to be drawn only where the opening is marked.
    constexpr D3DRENDERSTATETYPE kThroughStates[] = {
        D3DRS_STENCILENABLE, D3DRS_STENCILFUNC,      D3DRS_STENCILREF,
        D3DRS_STENCILMASK,   D3DRS_STENCILWRITEMASK, D3DRS_TWOSIDEDSTENCILMODE,
    };
    constexpr DWORD kThrough[] = {TRUE, D3DCMP_EQUAL, kMark, kMark, 0, FALSE};

    WeaponOverhaul::PixelShader g_depthShader{"eyepiece depth", g_eyepieceDepthPixelShader};
    WeaponOverhaul::PixelShader g_maskShader{"eyepiece mask", g_eyepieceMaskPixelShader};

    std::atomic<bool> g_enabled{true};

    // A point of the scope with the eye on the scope's axis, as a view-space slope, x right and y
    // up, and how far away it is in metres.
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
        // The reticle's centre at the scope's back end, and the opening's, with its radius as a slope.
        Spot back;
        Spot opening;
        float radius;
    };

    Scope g_scopes[kScopes] = {};
    size_t g_nextScope = 0;

    // This scope-up: the frame it was last seen in, the scope's draws as seen, the measurements so
    // far, and the scope it is cut as, none until one is known.
    uint32_t g_seenFrame = kNone;
    const void* g_vertices = nullptr;
    const void* g_reticle = nullptr;
    int g_attempts = 0;
    bool g_settled = false;
    bool g_measuredLast = false;
    Scope g_last = {};
    Scope* g_scope = nullptr;
    // The frame the opening was last marked in the stencil, and whether that worked.
    uint32_t g_markedFrame = kNone;
    bool g_marked = false;

    // What the draw under way changed, and what was there before.
    enum class Change { None, Cut, Grown };
    Change g_change = Change::None;
    float g_savedPlane[4] = {};
    DWORD g_savedPlanes = 0;
    float g_savedRows[8] = {};
    bool g_through = false;
    DWORD g_savedThrough[std::size(kThroughStates)] = {};

    struct Point {
        float x;
        float y;
    };

    struct Circle {
        float x;
        float y;
        float radius;
    };

    struct Centre {
        float x;
        float y;
    };

    double Determinant(double a, double b, double c, double d, double e, double f, double g,
                       double h, double i) {
        return a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }

    // The circle x² + y² + Dx + Ey + F = 0 nearest the points by least squares.
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

    float Metres(const WeaponOverhaul::WeaponDraws::Depth& depth, float stored) {
        return depth.depthOffset / (stored - depth.depthScale);
    }

    // From the depth read back: where the cut goes, and the two openings.
    bool Measure(const WeaponOverhaul::WeaponDraws::Depth& depth, const BYTE* bits, INT pitch,
                 Scope& scope) {
        const auto row = [&](UINT y) { return reinterpret_cast<const float*>(bits + y * pitch); };
        float nearest = 1.0f;
        for (UINT y = 0; y < depth.height; y++) {
            const float* stored = row(y);
            nearest = (std::min)(nearest, *std::min_element(stored, stored + depth.width));
        }
        if (nearest >= 1.0f) {
            return false;
        }
        scope.keptTo = Metres(depth, nearest) + kKept;
        const float cut = depth.depthScale + depth.depthOffset / scope.keptTo;

        // Along each ray the scope's far end is met first, then the part that is kept.
        const float centreX = depth.width / 2.0f;
        const float centreY = depth.height / 2.0f;
        std::vector<Point> engine;
        std::vector<Point> eyepiece;
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
                    farDistances += Metres(depth, stored);
                }
                if (stored <= cut) {
                    eyepiece.push_back({x - centreX, y - centreY});
                    openingDistances += Metres(depth, stored);
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

        // Slopes from pixels off the centre, taken back to where they are with the eye on the
        // scope's axis.
        const WeaponOverhaul::Aim::Offset lead = WeaponOverhaul::Aim::ScopeLead();
        const float horizontalScale = depth.verticalScale * depth.height / depth.width;
        const auto spot = [&](const Circle& circle, float distance) {
            return Spot{circle.x / centreX / horizontalScale + lead.right / distance,
                        -circle.y / centreY / depth.verticalScale + lead.up / distance, distance};
        };
        scope.back = spot(before, farDistances / engine.size());
        scope.opening = spot(after, openingDistances / eyepiece.size());
        scope.radius = after.radius / centreY / depth.verticalScale;
        scope.growth = after.radius / before.radius;
        return scope.back.distance > scope.opening.distance;
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
        scope.vertices = g_vertices;
        scope.reticle = g_reticle;
        g_scope = Find(g_vertices);
        if (g_scope == nullptr) {
            g_scope = &g_scopes[g_nextScope];
            g_nextScope = (g_nextScope + 1) % kScopes;
        }
        *g_scope = scope;
        FCSE::Logf("eyepiece: measured after %d frames: cut at %.3f m; the opening is %.3f m away "
                   "and grows x%.2f to a slope of %.3f; the reticle is %.3f m away",
                   g_attempts, scope.keptTo, scope.opening.distance, scope.growth, scope.radius,
                   scope.back.distance);
    }

    // Starts a scope-up over when the scope was not up the frame before.
    void Follow() {
        const uint32_t frame = WeaponOverhaul::Frame::Number();
        if (frame - g_seenFrame > 1) {
            g_vertices = nullptr;
            g_reticle = nullptr;
            g_attempts = 0;
            g_settled = false;
            g_measuredLast = false;
            g_scope = nullptr;
        }
        g_seenFrame = frame;
    }

    // Where a spot of the scope is this frame in clip space, the eye's lead moving it.
    Centre At(const Spot& spot, float horizontalScale, float verticalScale) {
        const WeaponOverhaul::Aim::Offset lead = WeaponOverhaul::Aim::ScopeLead();
        return {(spot.x - lead.right / spot.distance) * horizontalScale,
                (spot.y - lead.up / spot.distance) * verticalScale};
    }

    // Keeps what lies nearer than the scope's cut: stored depth at most the cut's.
    void Cut(IDirect3DDevice9* device, const float* projection) {
        device->GetClipPlane(0, g_savedPlane);
        device->GetRenderState(D3DRS_CLIPPLANEENABLE, &g_savedPlanes);
        const float plane[4] = {0.0f, 0.0f, -1.0f,
                                projection[10] * projection[14] + projection[11] / g_scope->keptTo};
        device->SetClipPlane(0, plane);
        device->SetRenderState(D3DRS_CLIPPLANEENABLE, g_savedPlanes | D3DCLIPPLANE0);
        g_change = Change::Cut;
    }

    // Sets kMark in the stencil inside the opening as the scope shadow found it, and clears it
    // everywhere else.
    bool Mark(IDirect3DDevice9* device, const float* projection) {
        IDirect3DPixelShader9* shader = g_maskShader.Get(device);
        IDirect3DTexture9* lens = WeaponOverhaul::ScopeShadow::FoundLens();
        if (shader == nullptr || lens == nullptr) {
            return false;
        }
        const float aspect = projection[5] / projection[0];
        WeaponOverhaul::ScreenDraw draw(device, 0, 1);
        device->SetPixelShader(shader);
        device->SetTexture(5, lens);
        device->SetRenderState(D3DRS_COLORWRITEENABLE, 0);
        device->SetRenderState(D3DRS_STENCILENABLE, TRUE);
        device->SetRenderState(D3DRS_TWOSIDEDSTENCILMODE, FALSE);
        device->SetRenderState(D3DRS_STENCILFUNC, D3DCMP_ALWAYS);
        device->SetRenderState(D3DRS_STENCILPASS, D3DSTENCILOP_REPLACE);
        device->SetRenderState(D3DRS_STENCILWRITEMASK, kMark);
        const float width = static_cast<float>(WeaponOverhaul::Frame::Width());
        const float height = static_cast<float>(WeaponOverhaul::Frame::Height());
        for (const bool inside : {false, true}) {
            const float constants[4] = {inside ? kMarkReach : kEverywhere, aspect, 0.0f, 0.0f};
            device->SetPixelShaderConstantF(0, constants, 1);
            device->SetRenderState(D3DRS_STENCILREF, inside ? kMark : 0);
            draw.Quad(0.0f, 0.0f, width, height);
        }
        return true;
    }

    // Scales the reticle's draw in clip space about its own centre and moves that centre onto the
    // opening's, swung with the shadow. Through the stencil, if the opening is marked.
    void Grow(IDirect3DDevice9* device, const float* projection) {
        const Centre back = At(g_scope->back, projection[0], projection[5]);
        const Centre opening = At(g_scope->opening, projection[0], projection[5]);
        const WeaponOverhaul::ScopeShadow::Shift swing = WeaponOverhaul::ScopeShadow::Swing();
        const float x = opening.x + swing.x * g_scope->radius * projection[0];
        const float y = opening.y - swing.y * g_scope->radius * projection[5];
        const float growth = g_scope->growth;
        float camera[16] = {};
        device->GetVertexShaderConstantF(kViewRotProjectionRegister, camera, 4);
        float rows[8] = {};
        for (int i = 0; i < 4; i++) {
            rows[i] = growth * camera[i] + (x - growth * back.x) * camera[12 + i];
            rows[4 + i] = growth * camera[4 + i] + (y - growth * back.y) * camera[12 + i];
        }
        std::copy_n(camera, std::size(g_savedRows), g_savedRows);
        device->SetVertexShaderConstantF(kViewRotProjectionRegister, rows, 2);
        g_change = Change::Grown;

        g_through = g_marked;
        if (g_through) {
            for (size_t i = 0; i < std::size(kThroughStates); i++) {
                device->GetRenderState(kThroughStates[i], &g_savedThrough[i]);
                device->SetRenderState(kThroughStates[i], kThrough[i]);
            }
        }
    }

    // A COM pointer's identity, without the reference the getter added.
    template <typename T>
    const void* Identity(T* object) {
        if (object != nullptr) {
            object->Release();
        }
        return object;
    }
}

void WeaponOverhaul::Eyepiece::OnDepthPass(const Frame::Pass& pass,
                                           const WeaponDraws::Depth& depth) {
    if (!g_enabled || !Aim::ScopeUp()) {
        return;
    }
    Follow();
    // Measured once the eye has settled into the scope, so the scope has stopped coming up.
    if (g_settled || g_attempts >= kAttempts || Aim::Scoped() < 1.0f || g_vertices == nullptr) {
        return;
    }
    g_attempts++;
    Scope scope = {};
    if (!ReadBack(pass, depth, scope)) {
        g_measuredLast = false;
    } else {
        g_settled = g_measuredLast && Agree(g_last, scope);
        g_measuredLast = true;
        g_last = scope;
        if (g_settled) {
            Keep(scope);
        }
    }
    if (!g_settled && g_attempts == kAttempts) {
        FCSE::Logf("eyepiece: the scope did not settle in %d frames, so it is drawn whole",
                   kAttempts);
    }
}

bool WeaponOverhaul::Eyepiece::BeforeGunDraw(IDirect3DDevice9* device, const float* projection,
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
    IDirect3DBaseTexture9* baseTexture = nullptr;
    device->GetTexture(0, &baseTexture);
    const void* texture = Identity(baseTexture);
    DWORD alphaTest = FALSE;
    device->GetRenderState(D3DRS_ALPHATESTENABLE, &alphaTest);

    // The reticle's is the scope's only alpha-tested draw.
    if (alphaTest) {
        g_vertices = vertices;
        g_reticle = texture;
    }
    if (g_scope == nullptr) {
        g_scope = Find(vertices);
    }
    if (g_scope == nullptr || vertices != g_scope->vertices) {
        return true;
    }
    if (alphaTest) {
        g_scope->reticle = texture;
    }
    // Beside the reticle some scopes draw markings blended, from the reticle's texture.
    if (!alphaTest && (texture == nullptr || texture != g_scope->reticle)) {
        Cut(device, projection);
        return true;
    }
    // Swung off the opening's centre, the reticle would read as housing in the gun's depth.
    if (depthPass) {
        return false;
    }
    const uint32_t frame = Frame::Number();
    if (g_markedFrame != frame) {
        g_markedFrame = frame;
        g_marked = Mark(device, projection);
    }
    Grow(device, projection);
    return true;
}

void WeaponOverhaul::Eyepiece::AfterGunDraw(IDirect3DDevice9* device) {
    if (g_change == Change::Cut) {
        device->SetClipPlane(0, g_savedPlane);
        device->SetRenderState(D3DRS_CLIPPLANEENABLE, g_savedPlanes);
    } else if (g_change == Change::Grown) {
        device->SetVertexShaderConstantF(kViewRotProjectionRegister, g_savedRows, 2);
        if (g_through) {
            for (size_t i = 0; i < std::size(kThroughStates); i++) {
                device->SetRenderState(kThroughStates[i], g_savedThrough[i]);
            }
        }
    }
    g_change = Change::None;
}

float WeaponOverhaul::Eyepiece::Hole(const WeaponDraws::Depth& depth) {
    if (!g_enabled || !Aim::ScopeUp() || g_scope == nullptr ||
        Frame::Number() - g_seenFrame > 1) {
        return 1.0f;
    }
    return depth.depthScale + depth.depthOffset / g_scope->keptTo;
}

void WeaponOverhaul::Eyepiece::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::Eyepiece::ReleaseDeviceObjects() {
    g_depthShader.Release();
    g_maskShader.Release();
    std::fill(std::begin(g_scopes), std::end(g_scopes), Scope{});
    g_scope = nullptr;
}
