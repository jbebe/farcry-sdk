#include "engine/image_file.h"

#include "engine/com.h"
#include "fcse_api.h"

#include <wincodec.h>

#include <algorithm>
#include <cstring>
#include <filesystem>
#include <vector>

extern "C" IMAGE_DOS_HEADER __ImageBase;

namespace {
    // A level's texels, four bytes each in the order D3DFMT_A8R8G8B8 keeps them, alpha multiplied
    // in.
    struct Level {
        UINT width = 0;
        UINT height = 0;
        std::vector<BYTE> texels;
    };

    bool Read(const wchar_t* file, Level& level) {
        IWICImagingFactory* factory = nullptr;
        IWICBitmapDecoder* decoder = nullptr;
        IWICBitmapFrameDecode* frame = nullptr;
        IWICFormatConverter* converter = nullptr;
        // The first version of the factory, which every Windows the game runs on has.
        bool read =
            SUCCEEDED(CoCreateInstance(CLSID_WICImagingFactory1, nullptr, CLSCTX_INPROC_SERVER,
                                       IID_PPV_ARGS(&factory))) &&
            SUCCEEDED(factory->CreateDecoderFromFilename(
                file, nullptr, GENERIC_READ, WICDecodeMetadataCacheOnDemand, &decoder)) &&
            SUCCEEDED(decoder->GetFrame(0, &frame)) &&
            SUCCEEDED(factory->CreateFormatConverter(&converter)) &&
            SUCCEEDED(converter->Initialize(frame, GUID_WICPixelFormat32bppPBGRA,
                                            WICBitmapDitherTypeNone, nullptr, 0.0,
                                            WICBitmapPaletteTypeCustom)) &&
            SUCCEEDED(converter->GetSize(&level.width, &level.height));
        if (read) {
            level.texels.resize(size_t{level.width} * level.height * 4);
            read = SUCCEEDED(converter->CopyPixels(nullptr, level.width * 4,
                                                   static_cast<UINT>(level.texels.size()),
                                                   level.texels.data()));
        }
        AimingOverhaul::Release(converter);
        AimingOverhaul::Release(frame);
        AimingOverhaul::Release(decoder);
        AimingOverhaul::Release(factory);
        return read;
    }

    // bin\plugins, the folder above the one this plugin is in.
    std::filesystem::path PluginsFolder() {
        wchar_t module[MAX_PATH] = {};
        GetModuleFileNameW(reinterpret_cast<HMODULE>(&__ImageBase), module, MAX_PATH);
        return std::filesystem::path(module).parent_path().parent_path();
    }

    bool Decode(const char* path, Level& level) {
        const std::filesystem::path file = PluginsFolder() / path;
        // The calling thread may have no COM yet; one it has already, in either model, serves.
        const HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        const bool read = Read(file.c_str(), level);
        if (SUCCEEDED(com)) {
            CoUninitialize();
        }
        return read;
    }

    // The next level down: each texel the average of the two by two above it.
    Level Halve(const Level& above) {
        Level level{(std::max)(above.width / 2, 1u), (std::max)(above.height / 2, 1u), {}};
        level.texels.resize(size_t{level.width} * level.height * 4);
        for (UINT y = 0; y < level.height; y++) {
            for (UINT x = 0; x < level.width; x++) {
                for (UINT channel = 0; channel < 4; channel++) {
                    UINT sum = 0;
                    for (UINT dy = 0; dy < 2; dy++) {
                        for (UINT dx = 0; dx < 2; dx++) {
                            const UINT fromX = (std::min)(x * 2 + dx, above.width - 1);
                            const UINT fromY = (std::min)(y * 2 + dy, above.height - 1);
                            sum += above.texels[(size_t{fromY} * above.width + fromX) * 4 + channel];
                        }
                    }
                    level.texels[(size_t{y} * level.width + x) * 4 + channel] =
                        static_cast<BYTE>((sum + 2) / 4);
                }
            }
        }
        return level;
    }
}

IDirect3DTexture9* AimingOverhaul::ImageFile::Texture(IDirect3DDevice9* device,
                                                      const char* path) {
    Level level;
    if (!Decode(path, level)) {
        FCSE::Logf("image file: bin\\plugins\\%s could not be read", path);
        return nullptr;
    }
    UINT levels = 1;
    while (((std::max)(level.width, level.height) >> levels) != 0) {
        levels++;
    }
    IDirect3DTexture9* texture = nullptr;
    if (FAILED(device->CreateTexture(level.width, level.height, levels, 0, D3DFMT_A8R8G8B8,
                                     D3DPOOL_MANAGED, &texture, nullptr))) {
        FCSE::Logf("image file: the device refused %s", path);
        return nullptr;
    }
    for (UINT at = 0; at < levels; at++) {
        if (at > 0) {
            level = Halve(level);
        }
        D3DLOCKED_RECT locked = {};
        if (FAILED(texture->LockRect(at, &locked, nullptr, 0))) {
            Release(texture);
            FCSE::Logf("image file: the device refused %s", path);
            return nullptr;
        }
        for (UINT row = 0; row < level.height; row++) {
            std::memcpy(static_cast<BYTE*>(locked.pBits) + size_t{row} * locked.Pitch,
                        &level.texels[size_t{row} * level.width * 4], size_t{level.width} * 4);
        }
        texture->UnlockRect(at);
    }
    return texture;
}
