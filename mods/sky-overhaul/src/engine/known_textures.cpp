#include "engine/known_textures.h"

#include "engine/crc32.h"
#include "fcse_api.h"

#include <algorithm>
#include <cstdint>
#include <iterator>

namespace {
    // Every material that binds one of these is rock, apart from the Dogon stone stairs: the
    // desert, savannah, jungle heart, jungle, woodland, wet river and river rock normal maps.
    constexpr uint32_t kRockNormals[] = {
        0x3BAA6EB6, 0xD451EE3B, 0xC22530C4, 0x2F994592, 0x586E465A, 0xFCD74329, 0x89969135,
    };

    constexpr UINT kLevelSize = 16;
    // Four rows of four DXT5 blocks.
    constexpr UINT kBlockRows = kLevelSize / 4;
    constexpr UINT kBlockRowBytes = kLevelSize / 4 * 16;

    // The private data a texture's verdict is kept under.
    constexpr GUID kVerdict = {
        0x6c1d6e0b, 0x5f7a, 0x4e52, {0x9b, 0x0b, 0x2f, 0x8e, 0x3c, 0x41, 0xa7, 0xd5}};

    bool g_refusalLogged = false;

    bool Fingerprinted(IDirect3DTexture9* texture) {
        D3DSURFACE_DESC top = {};
        if (FAILED(texture->GetLevelDesc(0, &top)) || top.Format != D3DFMT_DXT5 ||
            top.Width != top.Height) {
            return false;
        }
        DWORD level = 0;
        while ((top.Width >> level) > kLevelSize) {
            level++;
        }
        if ((top.Width >> level) != kLevelSize || level >= texture->GetLevelCount()) {
            return false;
        }

        D3DLOCKED_RECT rect = {};
        if (FAILED(texture->LockRect(level, &rect, nullptr, D3DLOCK_READONLY))) {
            if (!g_refusalLogged) {
                g_refusalLogged = true;
                FCSE::Logf("textures: a %ux%u DXT5 texture in pool %d cannot be read, so it is "
                           "never taken for rock",
                           top.Width, top.Height, static_cast<int>(top.Pool));
            }
            return false;
        }
        uint32_t crc = 0;
        for (UINT row = 0; row < kBlockRows; row++) {
            crc = SkyOverhaul::Crc32(static_cast<const uint8_t*>(rect.pBits) + row * rect.Pitch,
                                     kBlockRowBytes, crc);
        }
        texture->UnlockRect(level);
        return std::find(std::begin(kRockNormals), std::end(kRockNormals), crc) !=
               std::end(kRockNormals);
    }
}

bool SkyOverhaul::KnownTextures::IsRockNormal(IDirect3DBaseTexture9* texture) {
    uint8_t verdict = 0;
    DWORD size = sizeof(verdict);
    if (FAILED(texture->GetPrivateData(kVerdict, &verdict, &size))) {
        verdict = texture->GetType() == D3DRTYPE_TEXTURE &&
                  Fingerprinted(static_cast<IDirect3DTexture9*>(texture));
        texture->SetPrivateData(kVerdict, &verdict, sizeof(verdict), 0);
    }
    return verdict != 0;
}
