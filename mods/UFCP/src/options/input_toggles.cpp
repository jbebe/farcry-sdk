// Tap to aim or sprint, instead of holding the button down.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/input/inputtoggles.ixx.
//
// Both actions are hold-to-use: the button-down handler sets a bit in the pawn's state flags and
// button-up clears it, sprint's release also ending the run. Neither has a toggle mode, which the
// game offers for nothing else either.
//
// A tap is turned into a latch by suppressing the release: the down handlers are watched, and the
// up handlers are entered past the instruction that clears the bit - the joins at +11 and +18 skip
// exactly the AND that would drop it, and in sprint's case the end-of-run call after it.
//
// Latching has two ways to end badly, and both are guarded. A latch is only taken if the pawn was
// really doing the thing, so tapping sprint while standing still cannot arm a run that never
// starts; and once a frame the controller drops actions no longer in the action map, which is how
// the sights come down on entering a vehicle, so the latch is dropped with them.
//
// Three settings rather than two: a pad player and a mouse player disagree about aim often enough
// that one row for both would be wrong for someone. Sprint is keyboard-only because the pad already
// binds SprintLock, which toggles by itself.
#include "fcse_api.h"

#include "engine/input_device.h"

#include <windows.h>

#include <cstdint>

namespace {
    constexpr uintptr_t kControllerPawn = 0x20;
    constexpr uintptr_t kPawnStateBlock = 0x10;

    // What the frame asked for, cleared every frame, against what the pawn is actually doing.
    constexpr uintptr_t kInputState = 0x140;
    constexpr uintptr_t kCurrentState = 0x2D0;
    constexpr uintptr_t kStateFlags = 0x04;

    constexpr uint8_t kIronsightFlag = 0x08;
    constexpr uint8_t kSprintFlag = 0x40;

    // Presses shorter than this latch; longer ones behave exactly as they always did.
    constexpr uint64_t kTapMilliseconds = 250;

    // Into the release patterns: where the hook goes, and the join that skips the bit clear.
    constexpr ptrdiff_t kReleaseEntry = 2;
    constexpr ptrdiff_t kAimReleaseJoin = 11;
    constexpr ptrdiff_t kSprintReleaseJoin = 18;

    bool g_aimToggle = false;
    bool g_aimToggleController = false;
    bool g_sprintToggle = false;

    struct ToggleState {
        bool engaged;
        bool pressActive;
        bool pressedWhileEngaged;
        uint64_t pressTime;
    };

    ToggleState g_aimState;
    ToggleState g_sprintState;

    FCSE::Relocation<uint8_t*> g_aimPress{
        FCSE::Pattern("E8 ?? ?? ?? ?? 80 48 04 08 5F 5E 83 C4 10 C2 08 00")};
    FCSE::Relocation<uint8_t*> g_aimRelease{
        FCSE::Pattern("75 11 E8 ?? ?? ?? ?? 80 60 04 F7 5F 5E 83 C4 10 C2 08 00")};
    FCSE::Relocation<uint8_t*> g_sprintPress{
        FCSE::Pattern("E8 ?? ?? ?? ?? 80 48 04 40 5F 5E 83 C4 10 C2 08 00")};
    FCSE::Relocation<uint8_t*> g_sprintRelease{FCSE::Pattern(
        "75 18 E8 ?? ?? ?? ?? 80 60 04 BF 8B CE E8 ?? ?? ?? ?? 5F 5E 83 C4 10 C2 08 00")};
    FCSE::Relocation<uint8_t*> g_aimScope{
        FCSE::Pattern("84 C0 75 0C 8B 4E 20 E8 ?? ?? ?? ?? 80 60 04 F7")};
    FCSE::Relocation<uint8_t*> g_sprintScope{
        FCSE::Pattern("84 C0 75 13 8B 4E 20 E8 ?? ?? ?? ?? 80 60 04 BF 8B CE")};

    uintptr_t StateBlock(uintptr_t controller) {
        if (controller == 0) {
            return 0;
        }

        const uintptr_t pawn = *reinterpret_cast<uintptr_t*>(controller + kControllerPawn);
        if (pawn == 0) {
            return 0;
        }

        return *reinterpret_cast<uintptr_t*>(pawn + kPawnStateBlock);
    }

    bool StateFlagSet(uintptr_t block, uintptr_t which, uint8_t flag) {
        return block != 0 &&
               (*reinterpret_cast<uint8_t*>(block + which + kStateFlags) & flag) != 0;
    }

    void Reset(ToggleState& state) {
        state.engaged = false;
        state.pressActive = false;
    }

    void OnPress(ToggleState& state, bool enabled) {
        if (!enabled) {
            Reset(state);
            return;
        }

        if (state.pressActive) {
            return;
        }

        state.pressActive = true;
        state.pressTime = GetTickCount64();

        // A press while latched drops the toggle, whether it turns out to be a tap or a hold.
        state.pressedWhileEngaged = state.engaged;
    }

    // True to skip the engine's own button-up handling.
    bool OnRelease(ToggleState& state, bool enabled, bool mayLatch) {
        if (!enabled) {
            Reset(state);
            return false;
        }

        // A release with no press is a key repeat; keep swallowing it while latched.
        if (!state.pressActive) {
            return state.engaged;
        }

        state.pressActive = false;

        if (state.pressedWhileEngaged) {
            state.engaged = false;
            return false;
        }

        state.engaged = mayLatch && (GetTickCount64() - state.pressTime) <= kTapMilliseconds;
        return state.engaged;
    }

    bool AimEnabled() {
        return UFCP::IsPadActiveDevice() ? g_aimToggleController : g_aimToggle;
    }

    bool SprintEnabled() {
        return !UFCP::IsPadActiveDevice() && g_sprintToggle;
    }

    void AimPressHandler(FCSE_MidHookContext* /*ctx*/) {
        OnPress(g_aimState, AimEnabled());
    }

    void AimReleaseHandler(FCSE_MidHookContext* ctx) {
        if (OnRelease(g_aimState, AimEnabled(), true)) {
            ctx->eip = g_aimRelease.address() + kAimReleaseJoin;
        }
    }

    void SprintPressHandler(FCSE_MidHookContext* /*ctx*/) {
        OnPress(g_sprintState, SprintEnabled());
    }

    void SprintReleaseHandler(FCSE_MidHookContext* ctx) {
        const uintptr_t block = StateBlock(ctx->esi);
        const bool sprinting = StateFlagSet(block, kCurrentState, kSprintFlag);

        if (OnRelease(g_sprintState, SprintEnabled(), sprinting)) {
            ctx->eip = g_sprintRelease.address() + kSprintReleaseJoin;
        }
    }

    // AL is whether the action is still bound in the current map.
    void AimScopeHandler(FCSE_MidHookContext* ctx) {
        if (!g_aimState.engaged) {
            return;
        }

        const uintptr_t block = StateBlock(ctx->esi);
        if ((ctx->eax & 0xFF) == 0 || !StateFlagSet(block, kInputState, kIronsightFlag)) {
            Reset(g_aimState);
        }
    }

    void SprintScopeHandler(FCSE_MidHookContext* ctx) {
        if (!g_sprintState.engaged) {
            return;
        }

        // The run ended on its own, so sprinting again takes another tap.
        const uintptr_t block = StateBlock(ctx->esi);
        if ((ctx->eax & 0xFF) == 0 || !StateFlagSet(block, kCurrentState, kSprintFlag)) {
            Reset(g_sprintState);
        }
    }
}

void InstallInputTogglesHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_aimPress || !g_aimRelease || !g_sprintPress || !g_sprintRelease) {
        api->Log("input toggles: the aim and sprint handlers were not found in this build - tap to "
                 "toggle is unavailable");
        return;
    }

    api->MidHook(reinterpret_cast<void*>(g_aimPress.address()), &AimPressHandler);
    api->MidHook(reinterpret_cast<void*>(g_aimRelease.address() + kReleaseEntry),
                 &AimReleaseHandler);
    api->MidHook(reinterpret_cast<void*>(g_sprintPress.address()), &SprintPressHandler);
    api->MidHook(reinterpret_cast<void*>(g_sprintRelease.address() + kReleaseEntry),
                 &SprintReleaseHandler);

    // Without these a latch survives into a vehicle or a menu, so they are worth having but not
    // worth refusing the feature over.
    if (g_aimScope) {
        api->MidHook(reinterpret_cast<void*>(g_aimScope.address()), &AimScopeHandler);
    }
    if (g_sprintScope) {
        api->MidHook(reinterpret_cast<void*>(g_sprintScope.address()), &SprintScopeHandler);
    }
}

void __cdecl OnAimToggleChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_aimToggle = value->asCheckbox;
}

void __cdecl OnControllerAimToggleChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_aimToggleController = value->asCheckbox;
}

void __cdecl OnSprintToggleChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_sprintToggle = value->asCheckbox;
}
