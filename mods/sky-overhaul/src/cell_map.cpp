#include "cell_map.h"

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

    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DTexture9* g_texture = nullptr;
    bool g_refused = false;
    ULONGLONG g_drawnAt = 0;
    float g_texels[kTexels][kTexels][4];

    float Smooth(float t) {
        t = std::clamp(t, 0.0f, 1.0f);
        return t * t * (3.0f - 2.0f * t);
    }

    // How far a cell's outline bulges out toward `angle`, against a circle: a few lobes, turned by
    // `seed` so no two cells share an outline.
    float Lobes(float angle, float seed) {
        return 1.0f + 0.22f * std::sin(2.0f * angle + seed) + 0.16f * std::sin(3.0f * angle + seed * 1.7f) +
               0.1f * std::sin(5.0f * angle + seed * 2.9f);
    }

    // What a cell lays on a point, in units of its reach: x fills the layer, y how tall it stands,
    // z how dark and wet, w its anvil. Its cloud spreads half as far again as its rain.
    void Lay(const SkyOverhaul::Weather::Cell& cell, const float heading[2], float dx, float dy,
             float out[4]) {
        const float seed = cell.radius * 0.0137f;
        const float angle = std::atan2(dy, dx);
        const float distance = std::sqrt(dx * dx + dy * dy) / Lobes(angle, seed);
        const float along = dx * heading[0] + dy * heading[1];
        const float across = dy * heading[0] - dx * heading[1];
        const float strength = cell.strength;
        out[0] = (1.0f - Smooth((distance - 0.5f) / 1.0f)) * strength;
        out[1] = (1.0f - Smooth((distance - 0.3f) / 0.9f)) * strength;
        out[2] = (1.0f - Smooth((distance - 0.1f) / 0.8f)) * strength * (0.5f + 0.5f * cell.mature) *
                 (cell.strong ? 1.0f : 0.8f);
        const float a = (along - kAnvilAhead) / kAnvilAlong;
        const float c = across / kAnvilAcross;
        const float spread = std::sqrt(a * a + c * c) / Lobes(angle, seed + 1.3f);
        out[3] = (1.0f - Smooth((spread - 0.4f) / 0.6f)) * cell.mature * (std::min)(1.0f, 2.0f * strength);
    }

    // The outermost ring stays empty, which the clamped read carries out past the map's edge.
    void Draw(const SkyOverhaul::Weather::MapCellList& list) {
        std::memset(g_texels, 0, sizeof(g_texels));
        const float origin = kMiddle - 0.5f * kSpan;
        for (int i = 0; i < list.count; i++) {
            const SkyOverhaul::Weather::Cell& cell = list.cells[i];
            const float r = cell.radius;
            const float extent = r * (kAnvilAhead + kAnvilAlong) * kMostLobe;
            const int left = (std::max)(1, static_cast<int>((cell.x - extent - origin) / kTexel));
            const int right = (std::min)(kTexels - 2, static_cast<int>((cell.x + extent - origin) / kTexel));
            const int bottom = (std::max)(1, static_cast<int>((cell.y - extent - origin) / kTexel));
            const int top = (std::min)(kTexels - 2, static_cast<int>((cell.y + extent - origin) / kTexel));
            for (int v = bottom; v <= top; v++) {
                const float y = origin + (v + 0.5f) * kTexel;
                for (int u = left; u <= right; u++) {
                    const float x = origin + (u + 0.5f) * kTexel;
                    float laid[4];
                    Lay(cell, list.heading, (x - cell.x) / r, (y - cell.y) / r, laid);
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
