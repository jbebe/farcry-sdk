// Menu cursor: a slow mouse movement does not move the menu cursor at all, as if behind a deadzone.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/ui/menucursor.ixx.
//
// The cursor is moved down the gamepad stick's path: each frame by speed x axis x frame time,
// turned into whole pixels by a truncation toward zero. Whatever the hand moves below a pixel in one
// frame is thrown away, so a slow movement never arrives, and the higher the frame rate the wider
// that dead band. Steam Dunia.dll 0x104EFB42 and 0x104EFB6D, GOG 0x104E2292 and 0x104E22BD:
//
//     F3 0F 2C CC   cvttss2si ecx, xmm4   ; x pixels
//     F3 0F 2C D0   cvttss2si edx, xmm0   ; y pixels
//
// Each axis carries the fraction its truncation drops into the next frame, so the cursor travels as
// far as the hand did.
#include "fcse_api.h"

#include <cmath>
#include <cstdint>

namespace {
    // The truncations, 19 and 11 bytes into their matches.
    constexpr ptrdiff_t kXTruncated = 19;
    constexpr ptrdiff_t kYTruncated = 11;

    FCSE::Relocation<uint8_t*> g_cursorX{FCSE::Pattern(
        "F3 0F 10 86 88 00 00 00 0F 28 E0 F3 0F 59 E2 F3 0F 59 E3 F3 0F 2C CC")};
    FCSE::Relocation<uint8_t*> g_cursorY{FCSE::Pattern(
        "F3 0F 59 C1 F3 0F 59 C3 C1 E8 10 F3 0F 2C D0 66 2B C2")};

    float CarryFraction(float delta, float& fraction) {
        const float total = delta + fraction;
        fraction = total - std::trunc(total);
        return total;
    }

    void CursorXHandler(FCSE_MidHookContext* ctx) {
        static float fraction = 0.0f;
        ctx->xmm4.f32[0] = CarryFraction(ctx->xmm4.f32[0], fraction);
    }

    void CursorYHandler(FCSE_MidHookContext* ctx) {
        static float fraction = 0.0f;
        ctx->xmm0.f32[0] = CarryFraction(ctx->xmm0.f32[0], fraction);
    }
}

void ApplyMenuCursorFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_cursorX || !g_cursorY) {
        api->Log("menu cursor: the cursor's movement was not found in this build - not fixed");
        return;
    }

    if (api->MidHook(reinterpret_cast<void*>(g_cursorX.address() + kXTruncated), &CursorXHandler) &&
        api->MidHook(reinterpret_cast<void*>(g_cursorY.address() + kYTruncated), &CursorYHandler)) {
        api->Log("menu cursor follows slow mouse movement");
    }
}
