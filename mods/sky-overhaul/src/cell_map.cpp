#include "cell_map.h"

#include "blend.h"
#include "engine/com.h"
#include "fcse_api.h"
#include "weather/weather.h"

#include <windows.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>

namespace {
    // Texels across, and the square round the middle of the campaign world they cover, as
    // clouds.fx's CELL_MAP_SCALE and CELL_MAP_OFFSET read it.
    constexpr int kTexels = 128;
    constexpr float kMiddle = 2560.0f;
    constexpr float kSpan = 40960.0f;
    constexpr float kTexel = kSpan / kTexels;

    constexpr ULONGLONG kRedrawMilliseconds = 1000;

    // How far ahead of a cell's centre its anvil's middle lies and how far it reaches along and
    // across its track, against the cell's own reach.
    constexpr float kAnvilAhead = 1.0f;
    constexpr float kAnvilAlong = 2.0f;
    constexpr float kAnvilAcross = 1.4f;
    // The furthest Lobes bulges an outline.
    constexpr float kMostLobe = 1.48f;
    // Past these, in units of a cell's reach, its fill and its anvil lay nothing however far the
    // outline bulges: the fill ends at a distance of 1.5, the anvil at a spread of 1.
    constexpr float kFillEnds = 1.5f * kMostLobe;
    constexpr float kAnvilEnds = kMostLobe;

    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DTexture9* g_texture = nullptr;
    bool g_refused = false;
    ULONGLONG g_drawnAt = 0;
    float g_texels[kTexels][kTexels][4];

    using SkyOverhaul::Smooth;

    // The sines and cosines of the three turns Lobes takes: twice, three and five times an angle, or
    // a cell's seed scaled the way Lobes turns each.
    struct Turns {
        float sin[3];
        float cos[3];
    };

    Turns SeedTurns(float seed) {
        const float phases[3] = {seed, seed * 1.7f, seed * 2.9f};
        Turns turns;
        for (int i = 0; i < 3; i++) {
            turns.sin[i] = std::sin(phases[i]);
            turns.cos[i] = std::cos(phases[i]);
        }
        return turns;
    }

    // From the direction's own sine and cosine, so a texel needs no trigonometry.
    Turns AngleTurns(float dx, float dy, float length) {
        const float c = length > 0.0f ? dx / length : 1.0f;
        const float s = length > 0.0f ? dy / length : 0.0f;
        const float c2 = c * c - s * s;
        const float s2 = 2.0f * s * c;
        const float c3 = c * c2 - s * s2;
        const float s3 = s * c2 + c * s2;
        return {{s2, s3, s3 * c2 + c3 * s2}, {c2, c3, c3 * c2 - s3 * s2}};
    }

    // How far a cell's outline bulges out toward an angle, against a circle: a few lobes, turned by
    // a seed so no two cells share an outline.
    float Lobes(const Turns& angle, const Turns& seed) {
        const float lobe[3] = {0.22f, 0.16f, 0.1f};
        float bulge = 1.0f;
        for (int i = 0; i < 3; i++) {
            bulge += lobe[i] * (angle.sin[i] * seed.cos[i] + angle.cos[i] * seed.sin[i]);
        }
        return bulge;
    }

    // What a cell lays on a point, in units of its reach: x fills the layer, y how tall it stands,
    // z how dark and wet, w its anvil. Its cloud spreads half as far again as its rain. False when
    // it lays nothing there.
    bool Lay(const SkyOverhaul::Weather::Cell& cell, const Turns seeds[2], const float heading[2], float dx,
             float dy, float out[4]) {
        const float reach = dx * dx + dy * dy;
        const float a = (dx * heading[0] + dy * heading[1] - kAnvilAhead) / kAnvilAlong;
        const float c = (dy * heading[0] - dx * heading[1]) / kAnvilAcross;
        const float span = a * a + c * c;
        const bool fills = reach < kFillEnds * kFillEnds;
        const bool anvil = span < kAnvilEnds * kAnvilEnds && cell.mature > 0.0f;
        if (!fills && !anvil) {
            return false;
        }
        const float length = std::sqrt(reach);
        const Turns angle = AngleTurns(dx, dy, length);
        const float strength = cell.strength;
        const float distance = fills ? length / Lobes(angle, seeds[0]) : kFillEnds;
        out[0] = (1.0f - Smooth((distance - 0.5f) / 1.0f)) * strength;
        out[1] = (1.0f - Smooth((distance - 0.3f) / 0.9f)) * strength;
        out[2] = (1.0f - Smooth((distance - 0.1f) / 0.8f)) * strength * (0.5f + 0.5f * cell.mature) *
                 (cell.strong ? 1.0f : 0.8f);
        const float spread = anvil ? std::sqrt(span) / Lobes(angle, seeds[1]) : kAnvilEnds;
        out[3] = (1.0f - Smooth((spread - 0.4f) / 0.6f)) * cell.mature * (std::min)(1.0f, 2.0f * strength);
        return true;
    }

    // The outermost ring stays empty, which the clamped read carries out past the map's edge.
    void Draw(const SkyOverhaul::Weather::MapCellList& list) {
        std::memset(g_texels, 0, sizeof(g_texels));
        const float origin = kMiddle - 0.5f * kSpan;
        const float anvil = (std::max)(kAnvilAlong, kAnvilAcross) * kMostLobe;
        for (int i = 0; i < list.count; i++) {
            const SkyOverhaul::Weather::Cell& cell = list.cells[i];
            const float r = cell.radius;
            const float seed = r * 0.0137f;
            const Turns seeds[2] = {SeedTurns(seed), SeedTurns(seed + 1.3f)};
            // Round the fill's circle and the anvil's, which lies ahead along the heading.
            const float aheadX = kAnvilAhead * list.heading[0];
            const float aheadY = kAnvilAhead * list.heading[1];
            const float west = cell.x + r * (std::min)(-kFillEnds, aheadX - anvil);
            const float east = cell.x + r * (std::max)(kFillEnds, aheadX + anvil);
            const float south = cell.y + r * (std::min)(-kFillEnds, aheadY - anvil);
            const float north = cell.y + r * (std::max)(kFillEnds, aheadY + anvil);
            const int left = (std::max)(1, static_cast<int>((west - origin) / kTexel));
            const int right = (std::min)(kTexels - 2, static_cast<int>((east - origin) / kTexel));
            const int bottom = (std::max)(1, static_cast<int>((south - origin) / kTexel));
            const int top = (std::min)(kTexels - 2, static_cast<int>((north - origin) / kTexel));
            for (int v = bottom; v <= top; v++) {
                const float y = origin + (v + 0.5f) * kTexel;
                for (int u = left; u <= right; u++) {
                    const float x = origin + (u + 0.5f) * kTexel;
                    float laid[4];
                    if (!Lay(cell, seeds, list.heading, (x - cell.x) / r, (y - cell.y) / r, laid)) {
                        continue;
                    }
                    float* texel = g_texels[v][u];
                    for (int channel = 0; channel < 4; channel++) {
                        texel[channel] = (std::max)(texel[channel], laid[channel]);
                    }
                }
            }
        }
    }

    uint8_t Byte(float value) {
        return static_cast<uint8_t>(std::clamp(value, 0.0f, 1.0f) * 255.0f + 0.5f);
    }

    // Rows run north from the south edge, the way the shader's read of the map counts them.
    bool Upload() {
        D3DLOCKED_RECT rect = {};
        if (FAILED(g_texture->LockRect(0, &rect, nullptr, 0))) {
            return false;
        }
        for (int v = 0; v < kTexels; v++) {
            auto* row = static_cast<uint8_t*>(rect.pBits) + static_cast<size_t>(v) * rect.Pitch;
            for (int u = 0; u < kTexels; u++) {
                const float* texel = g_texels[v][u];
                row[u * 4 + 0] = Byte(texel[2]);
                row[u * 4 + 1] = Byte(texel[1]);
                row[u * 4 + 2] = Byte(texel[0]);
                row[u * 4 + 3] = Byte(texel[3]);
            }
        }
        g_texture->UnlockRect(0);
        return true;
    }
}

IDirect3DTexture9* SkyOverhaul::CellMap::Ensure(IDirect3DDevice9* device) {
    if (g_owner != device) {
        ReleaseDeviceObjects();
        g_owner = device;
        g_refused = false;
    }
    if (g_refused) {
        return nullptr;
    }
    const ULONGLONG now = GetTickCount64();
    if (g_texture != nullptr && now - g_drawnAt < kRedrawMilliseconds) {
        return g_texture;
    }
    if (g_texture == nullptr &&
        FAILED(device->CreateTexture(kTexels, kTexels, 1, 0, D3DFMT_A8R8G8B8, D3DPOOL_MANAGED,
                                     &g_texture, nullptr))) {
        g_refused = true;
        FCSE::Logf("clouds: this device would not take the cell map, so no cells will be drawn");
        return nullptr;
    }
    // A torn read keeps the cells drawn last.
    static Weather::MapCellList list = {};
    Weather::LatestCells(list);
    Draw(list);
    if (!Upload()) {
        Release(g_texture);
        g_refused = true;
        return nullptr;
    }
    g_drawnAt = now;
    return g_texture;
}

void SkyOverhaul::CellMap::ReleaseDeviceObjects() {
    Release(g_texture);
    g_owner = nullptr;
}
