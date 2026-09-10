#include "engine/noise.h"

#include "engine/com.h"
#include "engine/log.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <thread>
#include <vector>

namespace {
    // A cloud's body comes from the low frequencies and its edges from the high ones, so the two
    // are separate volumes: one large and sampled everywhere, one small and sampled only where
    // there is already something to erode.
    constexpr int kShapeSize = 128;
    constexpr int kDetailSize = 32;
    constexpr int kWeatherSize = 512;

    std::vector<uint8_t> g_shapeBytes;
    std::vector<uint8_t> g_detailBytes;
    std::vector<uint8_t> g_weatherBytes;
    std::atomic<bool> g_ready{false};

    IDirect3DDevice9* g_owner = nullptr;
    IDirect3DVolumeTexture9* g_shape = nullptr;
    IDirect3DVolumeTexture9* g_detail = nullptr;
    IDirect3DTexture9* g_weather = nullptr;
    bool g_refused = false;

    uint32_t Hash(int x, int y, int z, uint32_t seed) {
        uint32_t value = static_cast<uint32_t>(x) * 0x8DA6B343u +
                         static_cast<uint32_t>(y) * 0xD8163841u +
                         static_cast<uint32_t>(z) * 0xCB1AB31Fu + seed * 0x165667B1u;
        value ^= value >> 15;
        value *= 0x2C1B3C6Du;
        value ^= value >> 12;
        value *= 0x297A2D39u;
        value ^= value >> 15;
        return value;
    }

    float Unit(uint32_t hash) {
        return static_cast<float>(hash & 0xFFFFFFu) / 16777215.0f;
    }

    int Wrap(int value, int period) {
        const int wrapped = value % period;
        return wrapped < 0 ? wrapped + period : wrapped;
    }

    float Smooth(float t) {
        return t * t * (3.0f - 2.0f * t);
    }

    // Value noise on a lattice that repeats every `period` cells, which is what makes the volume
    // tile: a cloud drifting off one edge arrives back at the other without a seam.
    float Value(float x, float y, float z, int period, uint32_t seed) {
        const int x0 = static_cast<int>(std::floor(x));
        const int y0 = static_cast<int>(std::floor(y));
        const int z0 = static_cast<int>(std::floor(z));
        const float fx = Smooth(x - static_cast<float>(x0));
        const float fy = Smooth(y - static_cast<float>(y0));
        const float fz = Smooth(z - static_cast<float>(z0));

        float corner[8];
        for (int i = 0; i < 8; i++) {
            corner[i] = Unit(Hash(Wrap(x0 + (i & 1), period), Wrap(y0 + ((i >> 1) & 1), period),
                                  Wrap(z0 + ((i >> 2) & 1), period), seed));
        }

        const float x00 = corner[0] + (corner[1] - corner[0]) * fx;
        const float x10 = corner[2] + (corner[3] - corner[2]) * fx;
        const float x01 = corner[4] + (corner[5] - corner[4]) * fx;
        const float x11 = corner[6] + (corner[7] - corner[6]) * fx;
        const float y0v = x00 + (x10 - x00) * fy;
        const float y1v = x01 + (x11 - x01) * fy;
        return y0v + (y1v - y0v) * fz;
    }

    float ValueFbm(float x, float y, float z, int period, uint32_t seed) {
        float sum = 0.0f;
        float amplitude = 0.5f;
        float frequency = 1.0f;
        float total = 0.0f;
        for (int octave = 0; octave < 4; octave++) {
            sum += Value(x * frequency, y * frequency, z * frequency,
                         period * static_cast<int>(frequency), seed + octave) *
                   amplitude;
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2.0f;
        }
        return sum / total;
    }

    // Distance to the nearest of one feature point per cell, inverted so that the cell centres are
    // bright. Billowy where value noise is wispy, which is what gives a cloud its cauliflower.
    float Worley(float x, float y, float z, int grid, uint32_t seed) {
        const float sx = x * static_cast<float>(grid);
        const float sy = y * static_cast<float>(grid);
        const float sz = z * static_cast<float>(grid);
        const int cx = static_cast<int>(std::floor(sx));
        const int cy = static_cast<int>(std::floor(sy));
        const int cz = static_cast<int>(std::floor(sz));

        float nearest = 1.0e9f;
        for (int dz = -1; dz <= 1; dz++) {
            for (int dy = -1; dy <= 1; dy++) {
                for (int dx = -1; dx <= 1; dx++) {
                    const int px = cx + dx;
                    const int py = cy + dy;
                    const int pz = cz + dz;
                    const uint32_t hash =
                        Hash(Wrap(px, grid), Wrap(py, grid), Wrap(pz, grid), seed);
                    const float fx = static_cast<float>(px) + Unit(hash);
                    const float fy = static_cast<float>(py) + Unit(hash * 0x9E3779B1u + 1u);
                    const float fz = static_cast<float>(pz) + Unit(hash * 0x85EBCA6Bu + 2u);
                    const float ox = fx - sx;
                    const float oy = fy - sy;
                    const float oz = fz - sz;
                    const float squared = ox * ox + oy * oy + oz * oz;
                    if (squared < nearest) {
                        nearest = squared;
                    }
                }
            }
        }
        const float distance = std::sqrt(nearest);
        return 1.0f - (distance < 1.0f ? distance : 1.0f);
    }

    uint8_t Byte(float value) {
        return static_cast<uint8_t>(std::clamp(value, 0.0f, 1.0f) * 255.0f + 0.5f);
    }

    // A block of texels `size` wide and high and `depth` deep, each written by `texel` from where
    // it sits in the repeat, in the blue, green, red, alpha order Direct3D reads an A8R8G8B8 texel.
    template <class Texel>
    std::vector<uint8_t> Volume(int size, int depth, Texel texel) {
        std::vector<uint8_t> bytes(static_cast<size_t>(size) * size * depth * 4);
        uint8_t* at = bytes.data();
        for (int z = 0; z < depth; z++) {
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++, at += 4) {
                    texel(static_cast<float>(x) / size, static_cast<float>(y) / size,
                          static_cast<float>(z) / size, at);
                }
            }
        }
        return bytes;
    }

    void Generate() {
        g_shapeBytes =
            Volume(kShapeSize, kShapeSize, [](float u, float v, float w, uint8_t* texel) {
                texel[0] = Byte(Worley(u, v, w, 16, 303));
                texel[1] = Byte(Worley(u, v, w, 8, 202));
                texel[2] = Byte(ValueFbm(u * 4.0f, v * 4.0f, w * 4.0f, 4, 101));
                texel[3] = Byte(Worley(u, v, w, 4, 404));
            });

        g_detailBytes =
            Volume(kDetailSize, kDetailSize, [](float u, float v, float w, uint8_t* texel) {
                texel[0] = Byte(Worley(u, v, w, 16, 707));
                texel[1] = Byte(Worley(u, v, w, 8, 606));
                texel[2] = Byte(Worley(u, v, w, 4, 505));
                texel[3] = 255;
            });

        // Two independent fields: blue is how much cloud stands here and red how tall it grows,
        // where a shader reading .b and .r out of an A8R8G8B8 texel finds them.
        g_weatherBytes = Volume(kWeatherSize, 1, [](float u, float v, float, uint8_t* texel) {
            const float cover = ValueFbm(u * 6.0f, v * 6.0f, 0.5f, 6, 909);
            texel[0] = Byte(cover);
            texel[1] = Byte(cover);
            texel[2] = Byte(ValueFbm(u * 3.0f, v * 3.0f, 5.5f, 3, 808));
            texel[3] = 255;
        });

        g_ready.store(true, std::memory_order_release);
    }

    bool UploadVolume(IDirect3DDevice9* device, int size, const std::vector<uint8_t>& bytes,
                      IDirect3DVolumeTexture9** out) {
        if (FAILED(device->CreateVolumeTexture(size, size, size, 1, 0, D3DFMT_A8R8G8B8,
                                               D3DPOOL_MANAGED, out, nullptr))) {
            return false;
        }
        D3DLOCKED_BOX box = {};
        if (FAILED((*out)->LockBox(0, &box, nullptr, 0))) {
            SkyOverhaul::Release(*out);
            return false;
        }
        const size_t row = static_cast<size_t>(size) * 4;
        for (int z = 0; z < size; z++) {
            auto* slice =
                static_cast<uint8_t*>(box.pBits) + static_cast<size_t>(z) * box.SlicePitch;
            for (int y = 0; y < size; y++) {
                std::memcpy(slice + static_cast<size_t>(y) * box.RowPitch,
                            bytes.data() + (static_cast<size_t>(z) * size + y) * row, row);
            }
        }
        (*out)->UnlockBox(0);
        return true;
    }

    bool UploadPlane(IDirect3DDevice9* device, int size, const std::vector<uint8_t>& bytes,
                     IDirect3DTexture9** out) {
        if (FAILED(device->CreateTexture(size, size, 1, 0, D3DFMT_A8R8G8B8, D3DPOOL_MANAGED, out,
                                         nullptr))) {
            return false;
        }
        D3DLOCKED_RECT rect = {};
        if (FAILED((*out)->LockRect(0, &rect, nullptr, 0))) {
            SkyOverhaul::Release(*out);
            return false;
        }
        for (int y = 0; y < size; y++) {
            std::memcpy(static_cast<uint8_t*>(rect.pBits) + static_cast<size_t>(y) * rect.Pitch,
                        bytes.data() + static_cast<size_t>(y) * size * 4,
                        static_cast<size_t>(size) * 4);
        }
        (*out)->UnlockRect(0);
        return true;
    }
}

void SkyOverhaul::Noise::Start() {
    std::thread(&Generate).detach();
}

bool SkyOverhaul::Noise::Ensure(IDirect3DDevice9* device) {
    if (g_owner != device) {
        ReleaseDeviceObjects();
        g_owner = device;
        g_refused = false;
    }
    if (g_shape != nullptr) {
        return true;
    }
    if (g_refused || !g_ready.load(std::memory_order_acquire)) {
        return false;
    }

    if (!UploadVolume(device, kShapeSize, g_shapeBytes, &g_shape) ||
        !UploadVolume(device, kDetailSize, g_detailBytes, &g_detail) ||
        !UploadPlane(device, kWeatherSize, g_weatherBytes, &g_weather)) {
        ReleaseDeviceObjects();
        g_owner = device;
        g_refused = true;
        Logf("clouds: this device would not take the noise textures, so nothing will be drawn");
        return false;
    }

    Logf("clouds: noise ready, shape %d cubed, detail %d cubed", kShapeSize, kDetailSize);
    return true;
}

IDirect3DVolumeTexture9* SkyOverhaul::Noise::Shape() {
    return g_shape;
}

IDirect3DVolumeTexture9* SkyOverhaul::Noise::Detail() {
    return g_detail;
}

IDirect3DTexture9* SkyOverhaul::Noise::Weather() {
    return g_weather;
}

void SkyOverhaul::Noise::ReleaseDeviceObjects() {
    Release(g_shape);
    Release(g_detail);
    Release(g_weather);
    g_owner = nullptr;
}
