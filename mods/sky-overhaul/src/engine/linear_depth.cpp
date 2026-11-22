#include "engine/linear_depth.h"

#include "fcse_api.h"

#include <cstddef>
#include <cstdint>
#include <d3d9.h>
#include <iterator>

namespace {
    constexpr uint8_t kShipped = D3DFMT_A8R8G8B8;
    constexpr uint8_t kHalfFloat = D3DFMT_A16B16G16R16F;

    // CSceneRenderer::PrepareFrameGraph's push of the format for "Linear depth" and for
    // "LinearDepthMSAA", each as where its immediate sits inside the match.
    struct Site {
        const char* what;
        FCSE::Relocation<uint8_t*> match;
        size_t immediate;
    };

    Site g_sites[] = {
        {"the linear depth's format",
         FCSE::Relocation<uint8_t*>{FCSE::Pattern(
             "6A 00 6A 00 6A 00 6A 01 6A 01 6A 15 89 44 24 34 8B 44 24 4C 52 50 68")},
         11},
        {"the multisampled linear depth's format",
         FCSE::Relocation<uint8_t*>{FCSE::Pattern(
             "8B 94 24 E4 00 00 00 50 8B 43 38 6A 15 51 8B 48 64 52 68")},
         12},
    };
}

bool SkyOverhaul::LinearDepth::Install() {
    // The resolve fills one target from the other, so both change or neither.
    for (const Site& site : g_sites) {
        if (!site.match || site.match.get()[site.immediate] != kShipped) {
            FCSE::Logf("linear depth: %s was not found in this build - it stays eight bits",
                       site.what);
            return false;
        }
    }
    const FCSE_PluginAPI* api = FCSE::ApiPointer();
    for (size_t i = 0; i < std::size(g_sites); i++) {
        if (!api->Patch(g_sites[i].match.get() + g_sites[i].immediate, &kHalfFloat, 1)) {
            FCSE::Logf("linear depth: %s could not be changed - it stays eight bits",
                       g_sites[i].what);
            for (size_t j = 0; j < i; j++) {
                api->Patch(g_sites[j].match.get() + g_sites[j].immediate, &kShipped, 1);
            }
            return false;
        }
    }
    return true;
}
