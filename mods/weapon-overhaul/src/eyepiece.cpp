// A scope is measured from its depth, read back each frame once it has settled until two readings
// agree, and what was found is kept for that scope. The engine's housing is not drawn: the scope's
// body is laid over the finished frame instead, and the housing's first centimetre and the reticle,
// each drawn into a mask of its own, with it.
#include "eyepiece.h"

#include "engine/aim.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"
#include "scope_lens.h"

#include "blur_blur_ps.h"
#include "eyepiece_body_ps.h"
#include "eyepiece_depth_ps.h"
#include "eyepiece_housing_ps.h"
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

    // How much of the scope is measured as its eyepiece, and how much of a housing shown is drawn,
    // in metres past its nearest point.
    constexpr float kKept = 0.01f;
    constexpr float kHousingDepth = 0.03f;

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

    // The scope's body as it is drawn for every scope: its outer edge's radius, the mount's
    // half-width and its softness, as shares of the screen's height. Its rim, as a share of the
    // opening's radius, is a scope's own.
    constexpr float kBodyRadius = 0.4f;
    constexpr float kMountHalfWidth = 0.21f;
    constexpr float kBodySoftness = 0.0125f;
    constexpr float kThinRim = 0.15f;
    constexpr float kThickRim = 0.5f;
    // How far inside the opening the body is wholly black, so that its soft edge lies over the
    // shadow and the reticle rather than leaving a light seam between them and it.
    constexpr float kBodyOverlap = 0.005f;

    // The shipped sniper scopes, told apart by their housing's draw in the gun's depth pass, are a
    // plain ring with a rim of their own; any other scope also shows its own housing.
    struct Ring {
        UINT primitives;
        UINT vertices;
        float rim;
    };
    constexpr Ring kRings[] = {
        {2016, 9541, kThinRim},    // Dart Rifle
        {1488, 7332, kThinRim},    // M1903
        {2088, 10167, kThickRim},  // Dragunov, and the VSS that borrows its scope
        {1800, 11034, kThickRim},  // AS50
    };
    // The housing's softness: the reach of its blur as a share of the screen's height, and the
    // steps BlurPS takes either side.
    constexpr float kHousingSoftness = 2.0f * kBodySoftness;
    constexpr float kBlurSteps = 6.0f;

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
    WeaponOverhaul::PixelShader g_housingShader{"eyepiece housing", g_eyepieceHousingPixelShader};
    WeaponOverhaul::PixelShader g_blurShader{"eyepiece blur", g_blurPixelShader};

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
    // it is cut as, none until one is known; and its housing's draw, with the ring it makes, none
    // for a scope that shows its housing.
    struct ScopeUp {
        uint32_t number;
        const void* vertices;
        const void* reticle;
        int attempts;
        bool settled;
        std::optional<Scope> last;
        Scope* scope;
        UINT housingPrimitives;
        UINT housingVertices;
        const Ring* ring;
    };
    ScopeUp g_up = {};

    const Ring* RingFor(const WeaponOverhaul::WeaponDraws::Call& call) {
        for (const Ring& ring : kRings) {
            if (ring.primitives == call.primitiveCount && ring.vertices == call.numVertices) {
                return &ring;
            }
        }
        return nullptr;
    }

    float Rim() {
        return g_up.ring != nullptr ? g_up.ring->rim : kThinRim;
    }

    // The opening drawn, in screen heights.
    float OpeningRadius() {
        return kBodyRadius / (1.0f + Rim());
    }

    // A mask draws are sent into, one where nothing was drawn, at a share of the screen's size; the
    // frame it was last drawn in; and whether the device refused it, until a reset.
    struct Mask {
        const char* name;
        UINT share;
        IDirect3DDevice9* owner;
        WeaponOverhaul::Target target;
        UINT width;
        UINT height;
        uint32_t frame;
        bool refused;
    };
    Mask g_reticleMask = {"reticle", 1, nullptr, {}, 0, 0, WeaponOverhaul::Frame::kNever, false};
    Mask g_housingMask = {"housing", 2, nullptr, {}, 0, 0, WeaponOverhaul::Frame::kNever, false};
    // The housing's mask on its way to being softened.
    Mask g_housingScratch = {"softening", 2, nullptr, {}, 0, 0, WeaponOverhaul::Frame::kNever, false};

    // What the draw under way changed, and what was there before.
    Mask* g_into = nullptr;
    bool g_cut = false;
    float g_savedPlane[4] = {};
    DWORD g_savedPlanes = 0;
    bool g_scaled = false;
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
                   "and grows x%.2f to a slope of %.3f; the reticle is %.3f m away; the housing "
                   "draws %u primitives over %u vertices, %s",
                   g_up.attempts, scope.keptTo, scope.opening.distance, scope.growth, scope.radius,
                   scope.farEnd.distance, g_up.housingPrimitives, g_up.housingVertices,
                   g_up.ring != nullptr ? "a plain ring" : "shown");
    }

    // Starts over each time a scope comes up.
    void Follow() {
        const uint32_t number = WeaponOverhaul::Aim::ScopeUps();
        if (g_up.number != number) {
            g_up = {};
            g_up.number = number;
        }
    }

    // A point of the scope in clip space, x right and y up.
    Point At(const Spot& spot, const Projection& projection) {
        return {spot.x * projection.horizontalScale, spot.y * projection.verticalScale};
    }

    // The opening drawn, as a slope.
    float OpeningSlope(const Projection& projection) {
        return 2.0f * OpeningRadius() / projection.verticalScale;
    }

    // Scales the draw under way in clip space by `growth` about `from`, which it moves onto `to`.
    void Scale(IDirect3DDevice9* device, float growth, Point from, Point to) {
        float camera[16] = {};
        device->GetVertexShaderConstantF(kViewRotProjectionRegister, camera, 4);
        float rows[8] = {};
        for (int i = 0; i < 4; i++) {
            rows[i] = growth * camera[i] + (to.x - growth * from.x) * camera[12 + i];
            rows[4 + i] = growth * camera[4 + i] + (to.y - growth * from.y) * camera[12 + i];
        }
        std::copy_n(camera, std::size(g_savedRows), g_savedRows);
        device->SetVertexShaderConstantF(kViewRotProjectionRegister, rows, 2);
        g_scaled = true;
    }

    // Keeps what lies nearer than `metres`: stored depth at most theirs.
    void Cut(IDirect3DDevice9* device, const Projection& projection, float metres) {
        device->GetClipPlane(0, g_savedPlane);
        device->GetRenderState(D3DRS_CLIPPLANEENABLE, &g_savedPlanes);
        const float plane[4] = {0.0f, 0.0f, -1.0f, projection.Stored(metres)};
        device->SetClipPlane(0, plane);
        device->SetRenderState(D3DRS_CLIPPLANEENABLE, g_savedPlanes | D3DCLIPPLANE0);
        g_cut = true;
    }

    bool Ensure(IDirect3DDevice9* device, Mask& mask) {
        const UINT width = (WeaponOverhaul::Frame::Width() + mask.share - 1) / mask.share;
        const UINT height = (WeaponOverhaul::Frame::Height() + mask.share - 1) / mask.share;
        if (mask.owner == device && mask.target.texture != nullptr && mask.width == width &&
            mask.height == height) {
            return true;
        }
        WeaponOverhaul::Release(mask.target);
        if (mask.refused) {
            return false;
        }
        mask.owner = device;
        mask.width = width;
        mask.height = height;
        if (FAILED(WeaponOverhaul::CreateTarget(device, width, height, D3DFMT_A8R8G8B8,
                                                mask.target))) {
            WeaponOverhaul::Release(mask.target);
            mask.refused = true;
            FCSE::Logf("eyepiece: the device refused a %ux%u %s mask", width, height, mask.name);
            return false;
        }
        return true;
    }

    void Drop(Mask& mask) {
        WeaponOverhaul::Release(mask.target);
        mask.owner = nullptr;
        mask.refused = false;
        mask.frame = WeaponOverhaul::Frame::kNever;
    }

    // Sends the draw under way into `mask`, blended by `destBlend` so it leaves nought where it
    // lands, and clears the mask first each frame.
    bool IntoMask(IDirect3DDevice9* device, Mask& mask, DWORD destBlend) {
        if (!Ensure(device, mask)) {
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

        device->SetRenderTarget(0, mask.target.surface);
        for (DWORD i = 1; i < kTargets; i++) {
            if (g_savedTargets[i] != nullptr) {
                device->SetRenderTarget(i, nullptr);
            }
        }
        device->SetDepthStencilSurface(nullptr);
        const uint32_t frame = WeaponOverhaul::Frame::Number();
        if (mask.frame != frame) {
            mask.frame = frame;
            device->Clear(0, nullptr, D3DCLEAR_TARGET, 0xFFFFFFFF, 1.0f, 0);
        }
        const DWORD states[] = {D3DZB_FALSE, FALSE, FALSE, TRUE, FALSE, D3DBLEND_ZERO,
                                destBlend, D3DBLENDOP_ADD, kAllColour, FALSE};
        for (size_t i = 0; i < std::size(kMaskStates); i++) {
            device->SetRenderState(kMaskStates[i], states[i]);
        }
        g_into = &mask;
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
        g_into = nullptr;
    }

    // The housing's first centimetre, softened, darkening the finished frame by its cover.
    void InkHousing(IDirect3DDevice9* device, WeaponOverhaul::ScreenDraw& draw,
                    IDirect3DSurface9* frame) {
        IDirect3DPixelShader9* blur = g_blurShader.Get(device);
        IDirect3DPixelShader9* housing = g_housingShader.Get(device);
        if (g_housingMask.frame != WeaponOverhaul::Frame::Number() || blur == nullptr ||
            housing == nullptr || !Ensure(device, g_housingScratch)) {
            return;
        }
        const float width = static_cast<float>(g_housingMask.width);
        const float height = static_cast<float>(g_housingMask.height);
        const float step = kHousingSoftness * height / kBlurSteps;
        const float across[4] = {step / width, 0.0f, 0.0f, 0.0f};
        const float downward[4] = {0.0f, step / height, 0.0f, 0.0f};

        device->SetRenderState(D3DRS_ALPHABLENDENABLE, FALSE);
        device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
        device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
        device->SetPixelShader(blur);
        device->SetPixelShaderConstantF(1, across, 1);
        device->SetRenderTarget(0, g_housingScratch.target.surface);
        device->SetTexture(0, g_housingMask.target.texture);
        draw.Quad(0.0f, 0.0f, width, height);
        device->SetPixelShaderConstantF(1, downward, 1);
        device->SetRenderTarget(0, g_housingMask.target.surface);
        device->SetTexture(0, g_housingScratch.target.texture);
        draw.Quad(0.0f, 0.0f, width, height);

        device->SetRenderTarget(0, frame);
        device->SetTexture(0, g_housingMask.target.texture);
        device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
        device->SetPixelShader(housing);
        draw.Quad(0.0f, 0.0f, static_cast<float>(WeaponOverhaul::Frame::Width()),
                  static_cast<float>(WeaponOverhaul::Frame::Height()));
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
                                             const WeaponDraws::Call& call) {
    const bool depthPass = call.depthPass;
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
    } else if (depthPass && vertices != nullptr && vertices == g_up.vertices) {
        g_up.housingPrimitives = call.primitiveCount;
        g_up.housingVertices = call.numVertices;
        g_up.ring = RingFor(call);
    }
    if (g_up.scope == nullptr) {
        g_up.scope = Find(vertices);
    }
    if (g_up.scope == nullptr || vertices != g_up.scope->vertices) {
        return true;
    }
    const Scope& scope = *g_up.scope;
    // A plain ring is centred on the reticle where the engine draws it, a housing shown on its
    // opening. The reticle grows to the opening drawn as it was to the engine's lens.
    const Point farEnd = At(scope.farEnd, projection);
    const Point centre = g_up.ring != nullptr ? farEnd : At(scope.opening, projection);
    const float growth = OpeningSlope(projection) / (scope.radius / scope.growth);
    if (alphaTest) {
        g_up.scope->reticle = texture;
    } else {
        // Beside the reticle some scopes draw markings blended, from the reticle's texture.
        texture = BoundTexture(device);
        if (texture == nullptr || texture != scope.reticle) {
            // The housing is drawn only into its own mask, from the gun's depth pass, where it is
            // one opaque draw. Under a plain ring it is whole and grown with the reticle, so its
            // innermost hole, the engine's lens, is the opening drawn around the reticle; else
            // its first centimetres, shrunk to the opening drawn.
            if (!depthPass || !IntoMask(device, g_housingMask, D3DBLEND_ZERO)) {
                return false;
            }
            if (g_up.ring != nullptr) {
                Scale(device, growth, farEnd, centre);
            } else {
                Cut(device, projection, scope.keptTo - kKept + kHousingDepth);
                Scale(device, OpeningSlope(projection) / scope.radius, centre, centre);
            }
            return true;
        }
    }
    // The alpha-tested reticle is masked from the gun's depth pass, where its texels are tested
    // the same; what is blended is masked by its own alpha.
    if (alphaTest && !depthPass && !g_reticleMask.refused) {
        return false;
    }
    if (!IntoMask(device, g_reticleMask, alphaTest ? D3DBLEND_ZERO : D3DBLEND_INVSRCALPHA)) {
        return true;
    }
    Scale(device, growth, farEnd, centre);
    return true;
}

void WeaponOverhaul::Eyepiece::AfterGunDraw(IDirect3DDevice9* device) {
    if (g_scaled) {
        device->SetVertexShaderConstantF(kViewRotProjectionRegister, g_savedRows, 2);
        g_scaled = false;
    }
    if (g_cut) {
        device->SetClipPlane(0, g_savedPlane);
        device->SetRenderState(D3DRS_CLIPPLANEENABLE, g_savedPlanes);
        g_cut = false;
    }
    if (g_into != nullptr) {
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
        kInk, ScopeLens::kSmallest, kBodyRadius, 0.0f,
        OpeningRadius() - kBodyOverlap - kBodySoftness, kBodyRadius, kMountHalfWidth,
        kBodySoftness,
    };

    IDirect3DSurface9* frame = nullptr;
    device->GetRenderTarget(0, &frame);

    ScreenDraw draw(device, 0, 3);
    device->SetPixelShaderConstantF(0, constants, 3);
    device->SetTexture(5, lens);
    // Each darkens what is there by how much it covers it.
    device->SetRenderState(D3DRS_ALPHABLENDENABLE, TRUE);
    device->SetRenderState(D3DRS_SRCBLEND, D3DBLEND_ZERO);
    device->SetRenderState(D3DRS_DESTBLEND, D3DBLEND_INVSRCALPHA);
    device->SetRenderState(D3DRS_COLORWRITEENABLE, D3DCOLORWRITEENABLE_RED |
                                                       D3DCOLORWRITEENABLE_GREEN |
                                                       D3DCOLORWRITEENABLE_BLUE);
    device->SetPixelShader(body);
    draw.Quad(0.0f, 0.0f, width, height);
    if (g_up.ring == nullptr) {
        InkHousing(device, draw, frame);
    }
    Release(frame);

    IDirect3DPixelShader9* reticle =
        g_reticleMask.frame == Frame::Number() ? g_reticleShader.Get(device) : nullptr;
    if (reticle == nullptr) {
        return;
    }
    device->SetPixelShaderConstantF(1, constants + 4, 1);
    device->SetTexture(0, g_reticleMask.target.texture);
    device->SetSamplerState(0, D3DSAMP_MAGFILTER, D3DTEXF_LINEAR);
    device->SetSamplerState(0, D3DSAMP_MINFILTER, D3DTEXF_LINEAR);
    device->SetPixelShader(reticle);
    draw.Quad(0.0f, 0.0f, width, height);
}

WeaponOverhaul::ScopeLens::Walls WeaponOverhaul::Eyepiece::Walls(const WeaponDraws::Depth& depth) {
    // The housing's mask holds nought where the housing is, the gun's depth less than one.
    if (Active() && g_housingMask.frame == Frame::Number()) {
        // The glass may reach into the body, which covers whatever the housing does not.
        return {g_housingMask.target.texture, 0.5f, 1.0f + 2.0f * Rim()};
    }
    return {depth.texture, 1.0f, 1.05f};
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
    g_housingShader.Release();
    g_blurShader.Release();
    Drop(g_reticleMask);
    Drop(g_housingMask);
    Drop(g_housingScratch);
    std::fill(std::begin(g_scopes), std::end(g_scopes), Scope{});
    g_up.scope = nullptr;
}
