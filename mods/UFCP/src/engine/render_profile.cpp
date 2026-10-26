// The engine's render profile, the block its graphics settings live in.
#include "engine/render_profile.h"

#include "fcse_api.h"

namespace {
    // The camera's widescreen test, which loads the profile's global first.
    FCSE::Relocation<uint8_t*> g_widescreenTest{FCSE::Pattern(
        "8B 0D ?? ?? ?? ?? 83 79 40 00 0F 84 C4 00 00 00 E8 ?? ?? ?? ?? F3 0F 10 40 10")};

    // Into the match, the disp32 of `mov ecx,[render profile]`.
    constexpr ptrdiff_t kProfileGlobal = 2;

    uint8_t** g_global = nullptr;
}

namespace UFCP {

void FindRenderProfile() {
    if (!g_widescreenTest) {
        FCSE::ApiPointer()->Log("render profile: its global was not found in this build");
        return;
    }

    g_global = *reinterpret_cast<uint8_t***>(g_widescreenTest.address() + kProfileGlobal);
}

uint8_t* RenderProfile() {
    return g_global != nullptr ? *g_global : nullptr;
}

}
