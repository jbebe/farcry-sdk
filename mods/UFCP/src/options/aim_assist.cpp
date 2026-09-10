// Gamepad aim assist, and the ability to turn it off.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - the aim-assist half of
// source/input/looksensitivity.ixx. The polarity is inverted from the original, whose flag meant
// "skip the helpers"; here the setting reads as the player would expect and defaults to on, which
// is what the game shipped doing.
//
// Four helpers run from CActionMapPadFilter - follow-enemy, sticky, ironsight and shoot-correction
// - each guarded by its own flag byte at filter+0x15C..0x15F and each a 0x1E-byte block:
//
//     80 BE 5D 01 00 00 00   cmp byte ptr [esi+15Dh], 0
//     74 15                  je  <past the helper>
//
// The block is skipped rather than the flag cleared, because those flags are read elsewhere. Each
// site is hooked independently so one missing pattern costs one helper rather than all four.
//
// They are skipped for the mouse too, and that is deliberate: the filter runs off the action map,
// so with a pad merely connected the helpers pull the mouse's aim as well.
#include "fcse_api.h"

#include "engine/input_device.h"

#include <cstdint>

namespace {
    constexpr size_t kSiteCount = 4;

    // Offsets from the CMP to the block's start and to the instruction past its end.
    constexpr ptrdiff_t kHelperBlockStart = 9;
    constexpr ptrdiff_t kHelperBlockEnd = 0x1E;

    // One pattern per site: the blocks differ only in which scratch register they use.
    FCSE::Relocation<uint8_t*> g_sites[kSiteCount] = {
        FCSE::Relocation<uint8_t*>{FCSE::Pattern(
            "80 BE 5D 01 00 00 00 74 15 8D 4C 24 24 E8 ?? ?? ?? ?? 8D 4C 24 24 51 8B CE E8 ?? ?? "
            "?? ??")},
        FCSE::Relocation<uint8_t*>{FCSE::Pattern(
            "80 BE 5C 01 00 00 00 74 15 8D 4C 24 24 E8 ?? ?? ?? ?? 8D 54 24 24 52 8B CE E8 ?? ?? "
            "?? ??")},
        FCSE::Relocation<uint8_t*>{FCSE::Pattern(
            "80 BE 5F 01 00 00 00 74 15 8D 4C 24 24 E8 ?? ?? ?? ?? 8D 44 24 24 50 8B CE E8 ?? ?? "
            "?? ??")},
        FCSE::Relocation<uint8_t*>{FCSE::Pattern(
            "80 BE 5E 01 00 00 00 74 15 8D 4C 24 24 E8 ?? ?? ?? ?? 8D 4C 24 24 51 8B CE E8 ?? ?? "
            "?? ??")},
    };

    bool g_aimAssist = true;

    // The site is a template argument because a mid-hook handler is given no way to say which site
    // it is - on entry `eip` names the trampoline rather than the target.
    template <size_t Site>
    void SkipHelper(FCSE_MidHookContext* ctx) {
        if (!g_aimAssist || !UFCP::IsPadActiveDevice()) {
            ctx->eip = g_sites[Site].address() + kHelperBlockEnd;
        }
    }

    FCSE_MidHookHandler const kHandlers[kSiteCount] = {&SkipHelper<0>, &SkipHelper<1>,
                                                       &SkipHelper<2>, &SkipHelper<3>};
}

void InstallAimAssistHook() {
    size_t installed = 0;
    for (size_t site = 0; site < kSiteCount; ++site) {
        if (!g_sites[site]) {
            continue;
        }

        installed += FCSE::ApiPointer()->MidHook(
            reinterpret_cast<void*>(g_sites[site].address() + kHelperBlockStart), kHandlers[site]);
    }

    if (installed != kSiteCount) {
        FCSE::Logf("aim assist: %zu of %zu helpers can be skipped on this build", installed,
                   kSiteCount);
    }
}

void __cdecl OnAimAssistChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_aimAssist = value->asCheckbox;
    FCSE::ApiPointer()->Log(g_aimAssist ? "aim assist: on, the game's own"
                                        : "aim assist: off");
}
