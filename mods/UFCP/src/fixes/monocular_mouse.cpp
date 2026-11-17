// Monocular: with a mouse, the zoomed view turns as fast as the unzoomed one, so the smallest
// movement jumps across the picture.
//
// The mouse's look curve (Steam Dunia.dll 0x107D8B90, GOG 0x107CC2C0) slows the turn only while the
// effective iron-sight bit is set, scaling it by the zoom's field of view over the unzoomed 1.309
// radians. Steam 0x107D8C08, GOG 0x107CC338, with the pawn in ESI:
//
//     F6 40 04 08   test byte [eax+4], 8   ; effective data: down the sights?
//     74 4A         jz   unzoomed
//
// A gun's fire strategy mirrors that bit into the zoom's on byte, 0x0D into the skills, every frame.
// The monocular sets only the on byte and its field of view, so the mouse never slows; its
// fLookSensitivity reaches the gamepad alone. The test takes the on byte as well.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // The bit test, 7 bytes into the match.
    constexpr ptrdiff_t kSightsTest = 7;

    // The skills are *(pawn + 0x10) + 0xD8, and the zoom's on byte is 0x0D into them.
    constexpr size_t kPawnData = 0x10;
    constexpr size_t kZoomOn = 0xD8 + 0x0D;

    FCSE::Relocation<uint8_t*> g_sightsTest{
        FCSE::Pattern("8B CE E8 ?? ?? ?? ?? F6 40 04 08 74 ?? D9 44 24 1C 51 D9 1C 24 53 8B CD E8")};

    // Effective data whose flags hold only the iron-sight bit, for the test to read instead.
    alignas(4) const uint8_t g_downTheSights[8] = {0, 0, 0, 0, 0x08};

    void SightsTestHandler(FCSE_MidHookContext* ctx) {
        const auto* data = *reinterpret_cast<const uint8_t* const*>(ctx->esi + kPawnData);
        if (data[kZoomOn]) {
            ctx->eax = reinterpret_cast<uintptr_t>(g_downTheSights);
        }
    }
}

void ApplyMonocularMouseFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_sightsTest) {
        api->Log("monocular mouse: the mouse's iron-sight test was not found in this build - not fixed");
        return;
    }

    if (api->MidHook(g_sightsTest.get() + kSightsTest, &SightsTestHandler)) {
        api->Log("monocular turns with the mouse as a scope does");
    }
}
