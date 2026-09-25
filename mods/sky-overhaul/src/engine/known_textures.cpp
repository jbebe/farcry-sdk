#include "engine/known_textures.h"

#include "engine/crc32.h"
#include "fcse_api.h"

#include <cstdint>
#include <iterator>

namespace {
    struct Named {
        uint32_t fingerprint;
        const char* name;
    };

    // Every material that binds one of these is rock, apart from the Dogon stone stairs.
    constexpr Named kRockNormals[] = {
        {0x3BAA6EB6, "dessert_mountain_rock_n"},  {0xD451EE3B, "savannah_mountain_rock_n"},
        {0xC22530C4, "jungle_heart_rock_n"},      {0x2F994592, "jungle_mountain_rock_n"},
        {0x586E465A, "woodland_mountain_rock_n"}, {0xFCD74329, "drock19_n"},
        {0x89969135, "riverrock_n"},
    };

    constexpr UINT kLevelSize = 16;
    // Four rows of four DXT5 blocks.
    constexpr UINT kBlockRows = kLevelSize / 4;
    constexpr UINT kBlockRowBytes = kLevelSize / 4 * 16;

    // The private data a texture's verdict is kept under.
    constexpr GUID kVerdict = {
        0x6c1d6e0b, 0x5f7a, 0x4e52, {0x9b, 0x0b, 0x2f, 0x8e, 0x3c, 0x41, 0xa7, 0xd5}};

    // One past the index into kRockNormals, so zero is any other texture.
    using Verdict = uint8_t;

    bool g_refusalLogged = false;

    Verdict Fingerprint(IDirect3DTexture9* texture) {
        D3DSURFACE_DESC top = {};
        if (FAILED(texture->GetLevelDesc(0, &top)) || top.Format != D3DFMT_DXT5 ||
            top.Width != top.Height) {
            return 0;
        }
        const DWORD levels = texture->GetLevelCount();
        for (DWORD level = 0; level < levels; level++) {
            D3DSURFACE_DESC desc = {};
            if (FAILED(texture->GetLevelDesc(level, &desc)) || desc.Width > kLevelSize) {
                continue;
            }
            if (desc.Width < kLevelSize) {
                return 0;
            }
            D3DLOCKED_RECT rect = {};
            if (FAILED(texture->LockRect(level, &rect, nullptr, D3DLOCK_READONLY))) {
                if (!g_refusalLogged) {
                    g_refusalLogged = true;
                    FCSE::Logf("textures: a %ux%u DXT5 texture in pool %d cannot be read, so "
                               "it is never taken for rock",
                               top.Width, top.Height, static_cast<int>(top.Pool));
                }
                return 0;
            }
            uint32_t crc = 0;
            for (UINT row = 0; row < kBlockRows; row++) {
                crc = SkyOverhaul::Crc32(static_cast<const uint8_t*>(rect.pBits) + row * rect.Pitch,
                                         kBlockRowBytes, crc);
            }
            texture->UnlockRect(level);
            for (size_t i = 0; i < std::size(kRockNormals); i++) {
                if (kRockNormals[i].fingerprint == crc) {
                    return static_cast<Verdict>(i + 1);
                }
            }
            return 0;
        }
        return 0;
    }
}

const char* SkyOverhaul::KnownTextures::RockNormal(IDirect3DBaseTexture9* texture) {
    Verdict verdict = 0;
    DWORD size = sizeof(verdict);
    if (FAILED(texture->GetPrivateData(kVerdict, &verdict, &size))) {
        verdict = texture->GetType() == D3DRTYPE_TEXTURE
                      ? Fingerprint(static_cast<IDirect3DTexture9*>(texture))
                      : 0;
        texture->SetPrivateData(kVerdict, &verdict, sizeof(verdict), 0);
    }
    return verdict == 0 ? nullptr : kRockNormals[verdict - 1].name;
}
