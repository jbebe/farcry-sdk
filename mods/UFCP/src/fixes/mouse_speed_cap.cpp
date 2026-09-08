// Mouse speed cap: the view stops keeping up with a fast flick.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/input/mousespeed.ixx.
//
// Moving the mouse quickly turns the view less far than moving it the same distance slowly, so a
// flick undershoots and the game feels like it is fighting the hand. The cause is that
// CActionMapCurveFilter's output ceiling, `maxOutput`, is applied to the mouse as well as to the
// gamepad. A thumbstick genuinely has a maximum deflection and wants one; a mouse does not, and
// saturating it is the defect.
//
// The filter is shared, so the site cannot simply be defeated - it has to know which device it is
// running for. The filter's own id sits at filter+0x04, written by its constructor, and the mouse's
// is 0xDE7832DA:
//
//     F3 0F 10 4F 0C   movss xmm1, dword ptr [edi+0Ch]   ; maxOutput
//     F3 0F 5E C2      divss xmm0, xmm2
//     F3 0F 59 47 10   mulss xmm0, dword ptr [edi+10h]
//
// Raising xmm1 to a value the filter can never reach lifts the ceiling for the mouse alone, leaving
// every gamepad filter clamped exactly as it shipped. The ceiling is raised rather than removed
// because the comparison that follows still has to have something to compare against.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // Far enough above any real value that the clamp can never bind, and finite so the comparison
    // behind it still behaves.
    constexpr float kNoCap = 1000000.0f;

    // CActionMapCurveFilter's id for the mouse, at filter+0x04.
    constexpr uint32_t kMouseFilterId = 0xDE7832DA;

    // The load of maxOutput into xmm1, five bytes into the match.
    constexpr ptrdiff_t kMaxOutputLoaded = 5;

    FCSE::Relocation<uint8_t*> g_maxOutput{FCSE::Pattern(
        "F3 0F 10 4F 0C F3 0F 5E C2 F3 0F 59 47 10 F3 0F 59 C4 0F 28 F8 0F 54 FD 0F 2F F9 76")};

    void MaxOutputHandler(FCSE_MidHookContext* ctx) {
        if (*reinterpret_cast<uint32_t*>(ctx->edi + 4) != kMouseFilterId) {
            return;
        }

        ctx->xmm1.f32[0] = kNoCap;
    }
}

void ApplyMouseSpeedCapFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_maxOutput) {
        api->Log("mouse speed cap: the action map's output filter was not found in this build - "
                 "not fixed");
        return;
    }

    if (api->MidHook(reinterpret_cast<void*>(g_maxOutput.address() + kMaxOutputLoaded),
                     &MaxOutputHandler)) {
        api->Log("mouse speed cap lifted");
    }
}
