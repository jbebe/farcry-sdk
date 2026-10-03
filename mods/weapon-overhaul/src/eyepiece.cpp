// The eyepiece. When a scope comes up its depth is read back once: the nearest point is the tube's
// eye end, and two circles are fitted along rays from the screen's centre, the opening as the
// engine draws it and the opening once the tube is cut.
#include "eyepiece.h"

#include "engine/aim.h"
#include "engine/render_target.h"
#include "engine/screen_draw.h"
#include "engine/shader.h"
#include "fcse_api.h"

#include "eyepiece_depth_ps.h"

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
    // Frames a scope-up is measured in before it is left as the engine draws it.
    constexpr int kAttempts = 5;

    constexpr UINT kProjectionRegister = 8;
    constexpr uint32_t kNone = 0xFFFFFFFFu;

    WeaponOverhaul::PixelShader g_depthShader{"eyepiece depth", g_eyepieceDepthPixelShader};

    std::atomic<bool> g_enabled{true};

    // The frame the scope was last seen up in, how many times this scope-up has been measured, and
    // whether one worked.
    uint32_t g_seenFrame = kNone;
    int g_attempts = 0;
    bool g_valid = false;
    // The distance the scope is cut at, in metres.
    float g_keptTo = 0.0f;
    // How much the reticle grows, its centre at rest in clip space, x right and y up, and how far
    // away it is in metres.
    float g_growth = 1.0f;
    float g_centreX = 0.0f;
    float g_centreY = 0.0f;
    float g_reticleDistance = 0.0f;
    // The scope's vertex buffer and the reticle's texture, taken from the reticle's draw, which is
    // the scope's only alpha-tested one. Compared, never dereferenced.
    const void* g_scope = nullptr;
    const void* g_reticle = nullptr;

    // What the draw under way changed, and what was there before.
    enum class Change { None, Cut, Grown };
    Change g_change = Change::None;
    float g_savedPlane[4] = {};
    DWORD g_savedPlanes = 0;
    float g_savedRows[8] = {};

    struct Point {
        float x;
        float y;
    };

    struct Circle {
        float x;
        float y;
        float radius;
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
    bool Measure(const WeaponOverhaul::WeaponDraws::Depth& depth, const BYTE* bits, INT pitch) {
        const auto row = [&](UINT y) { return reinterpret_cast<const float*>(bits + y * pitch); };
        float nearest = 1.0f;
        for (UINT y = 0; y < depth.height; y++) {
            const float* stored = row(y);
            nearest = (std::min)(nearest, *std::min_element(stored, stored + depth.width));
        }
        if (nearest >= 1.0f) {
            FCSE::ApiPointer()->Log("eyepiece: no scope in the gun's depth");
            return false;
        }
        g_keptTo = Metres(depth, nearest) + kKept;
        const float cut = depth.depthScale + depth.depthOffset / g_keptTo;

        // Along each ray the scope's far end is met first, then the part that is kept.
        const float centreX = depth.width / 2.0f;
        const float centreY = depth.height / 2.0f;
        std::vector<Point> engine;
        std::vector<Point> eyepiece;
        float farthest = 0.0f;
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
                    farthest += Metres(depth, stored);
                }
                if (stored <= cut) {
                    eyepiece.push_back({x - centreX, y - centreY});
                    break;
                }
            }
        }
        Circle before = {};
        Circle after = {};
        if (!Fit(engine, before) || !Fit(eyepiece, after)) {
            FCSE::Logf("eyepiece: the openings could not be fitted, %zu and %zu edge points",
                       engine.size(), eyepiece.size());
            return false;
        }
        const float off = kFurthestOff * depth.height;
        if (std::hypot(before.x, before.y) > off || std::hypot(after.x, after.y) > off ||
            after.radius <= before.radius || after.radius > kMostGrowth * before.radius) {
            FCSE::Logf("eyepiece: not a scope's openings: %.3f at (%.3f, %.3f) and %.3f at "
                       "(%.3f, %.3f), in screen heights",
                       before.radius / depth.height, before.x / depth.height,
                       before.y / depth.height, after.radius / depth.height,
                       after.x / depth.height, after.y / depth.height);
            return false;
        }

        g_growth = after.radius / before.radius;
        g_reticleDistance = farthest / engine.size();
        // The centre as found, taken back to where it is with the eye on the scope's axis.
        const WeaponOverhaul::Aim::Offset lead = WeaponOverhaul::Aim::ScopeLead();
        const float horizontalScale = depth.verticalScale * depth.height / depth.width;
        g_centreX = before.x / centreX + lead.right * horizontalScale / g_reticleDistance;
        g_centreY = -before.y / centreY + lead.up * depth.verticalScale / g_reticleDistance;
        FCSE::Logf("eyepiece: cut at %.3f m; the opening grows from %.3f to %.3f of the screen's "
                   "height, x%.2f; the reticle is %.3f m away",
                   g_keptTo, before.radius / depth.height, after.radius / depth.height, g_growth,
                   g_reticleDistance);
        return true;
    }

    // Reads the depth back through a float target, which the device can copy to memory.
    bool ReadBack(const WeaponOverhaul::Frame::Pass& pass,
                  const WeaponOverhaul::WeaponDraws::Depth& depth) {
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
            measured = Measure(depth, static_cast<const BYTE*>(locked.pBits), locked.Pitch);
            memory->UnlockRect();
        } else {
            FCSE::ApiPointer()->Log("eyepiece: the depth could not be read back");
        }
        WeaponOverhaul::Release(memory);
        WeaponOverhaul::Release(copy);
        return measured;
    }

    bool Current() {
        return g_valid && WeaponOverhaul::Aim::ScopeUp() &&
               WeaponOverhaul::Frame::Number() - g_seenFrame <= 1;
    }

    // Keeps what lies nearer than g_keptTo: stored depth at most its.
    void Cut(IDirect3DDevice9* device, const float* projection) {
        device->GetClipPlane(0, g_savedPlane);
        device->GetRenderState(D3DRS_CLIPPLANEENABLE, &g_savedPlanes);
        const float plane[4] = {0.0f, 0.0f, -1.0f,
                                projection[10] * projection[14] + projection[11] / g_keptTo};
        device->SetClipPlane(0, plane);
        device->SetRenderState(D3DRS_CLIPPLANEENABLE, g_savedPlanes | D3DCLIPPLANE0);
        g_change = Change::Cut;
    }

    // Scales the draw's clip-space x and y about the reticle's centre, which the eye's lead moves.
    void Grow(IDirect3DDevice9* device, const float* projection) {
        const WeaponOverhaul::Aim::Offset lead = WeaponOverhaul::Aim::ScopeLead();
        const float x = g_centreX - lead.right * projection[0] / g_reticleDistance;
        const float y = g_centreY - lead.up * projection[5] / g_reticleDistance;
        float rows[8] = {};
        for (int i = 0; i < 4; i++) {
            rows[i] = g_growth * projection[i] + (1.0f - g_growth) * x * projection[12 + i];
            rows[4 + i] = g_growth * projection[4 + i] + (1.0f - g_growth) * y * projection[12 + i];
        }
        std::copy_n(projection, std::size(g_savedRows), g_savedRows);
        device->SetVertexShaderConstantF(kProjectionRegister, rows, 2);
        g_change = Change::Grown;
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
    const uint32_t frame = Frame::Number();
    if (frame - g_seenFrame > 1) {
        g_attempts = 0;
        g_valid = false;
        g_scope = nullptr;
        g_reticle = nullptr;
    }
    g_seenFrame = frame;
    if (!g_valid && g_attempts < kAttempts) {
        g_attempts++;
        g_valid = ReadBack(pass, depth);
    }
}

void WeaponOverhaul::Eyepiece::BeforeGunDraw(IDirect3DDevice9* device, const float* projection) {
    if (!g_enabled || !Current()) {
        return;
    }
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

    if (alphaTest) {
        g_scope = vertices;
        g_reticle = texture;
    }
    if (vertices == nullptr || vertices != g_scope) {
        return;
    }
    // Beside the reticle some scopes draw markings blended, from the reticle's texture.
    if (alphaTest || (texture != nullptr && texture == g_reticle)) {
        Grow(device, projection);
    } else {
        Cut(device, projection);
    }
}

void WeaponOverhaul::Eyepiece::AfterGunDraw(IDirect3DDevice9* device) {
    if (g_change == Change::Cut) {
        device->SetClipPlane(0, g_savedPlane);
        device->SetRenderState(D3DRS_CLIPPLANEENABLE, g_savedPlanes);
    } else if (g_change == Change::Grown) {
        device->SetVertexShaderConstantF(kProjectionRegister, g_savedRows, 2);
    }
    g_change = Change::None;
}

void WeaponOverhaul::Eyepiece::SetEnabled(bool enabled) {
    g_enabled = enabled;
}

void WeaponOverhaul::Eyepiece::ReleaseDeviceObjects() {
    g_depthShader.Release();
}
