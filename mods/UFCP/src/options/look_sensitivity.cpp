// Look sensitivity, past the in-game slider's ceiling, and separately per device.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - the sensitivity half of
// source/input/looksensitivity.ixx, and the keyboard and mouse half of source/input/inputdevice.ixx.
//
// The game keeps one Sensitivity property, at GameProfile+0xB0, and applies it to mouse and gamepad
// alike - so a player who wants a fast mouse and a slow stick cannot have both, and neither can go
// past what the slider offers. CPawnInputListener's look handler is the only site that reads it:
//
//     F3 0F 10 88 B0 00 00 00   movss xmm1, dword ptr [eax+0B0h]   ; Sensitivity
//     8B 88 B8 00 00 00         mov   ecx, dword ptr [eax+0B8h]
//
// Scaling xmm1 there leaves the stored property, and therefore the in-game slider, untouched. Which
// multiplier applies depends on what the player is currently looking with, which is a narrower
// question than which device they last touched: pushing the right stick past the engine's own dead
// zone claims the look, and any mouse or keyboard input hands it back. The action map has lost which
// device sent what by then, so the hand-back is read off the keyboard and mouse drivers' own entries.
//
// Both sliders are in hundredths, because FCSE has no float setting; 100 is the game's own value
// and the point at which each disables itself.
#include "fcse_api.h"

#include <cstdint>

namespace {
    // The two look axes, as the filter numbers them.
    constexpr uintptr_t kFirstLookAxis = 6;
    constexpr uintptr_t kLastLookAxis = 7;

    // Where the property has been loaded into xmm1, 22 bytes into the match.
    constexpr ptrdiff_t kSensitivityLoaded = 22;

    // Past the engine's dead-zone compare on the right stick, 12 bytes in.
    constexpr ptrdiff_t kLookAxisTested = 12;

    float g_mouseSensitivity = 1.0f;
    float g_controllerSensitivity = 1.0f;

    bool g_padIsLookDevice = false;

    FCSE::Relocation<uint8_t*> g_lookHandler{FCSE::Pattern(
        "F3 0F 10 88 B0 00 00 00 8B 88 B8 00 00 00 F3 0F 10 15 ?? ?? ?? ?? F3 0F 11 4C 24 08")};

    FCSE::Relocation<uint8_t*> g_padLookAxis{
        FCSE::Pattern("0F 28 C8 0F 54 CA F3 0F 10 51 08 56 0F 2F D1 0F 57 C9 57")};

    // CInputDriverKeyboard::OnKey(keyStates, sink, keyIndex).
    FCSE::Relocation<uint8_t*> g_keyboardOnKey{
        FCSE::Pattern("8B 44 24 0C 83 EC 30 8D 14 C5 00 00 00 00 2B D0 56 8D 34 91")};

    // CInputDriverMouse::OnButton(sink, frameState, buttonIndex, controlId).
    FCSE::Relocation<uint8_t*> g_mouseOnButton{
        FCSE::Pattern("8B 44 24 08 83 EC 30 53 56 57 8B 7C 24 48 8A 5C 38 0C 8B F1 C0 EB 07")};

    // CInputDriverMouse::OnMove, given x and y accumulated over the frame.
    FCSE::Relocation<uint8_t*> g_mouseOnMove{
        FCSE::Pattern("8B 44 24 08 F3 0F 2A 10 F3 0F 2A 58 04 83 EC 14 53 55 8B E9 0F 2E 55 18")};

    // DirectInput reports a byte per key and per button, high bit set while held.
    constexpr uint8_t kPressedBit = 0x80;

    // Button bytes in the mouse driver's frame state, after the x, y and wheel deltas.
    constexpr ptrdiff_t kMouseButtons = 0x0C;
    constexpr int32_t kMouseButtonCount = 8;
    constexpr int32_t kKeyboardKeyCount = 256;

    void LookSensitivityHandler(FCSE_MidHookContext* ctx) {
        ctx->xmm1.f32[0] *= g_padIsLookDevice ? g_controllerSensitivity : g_mouseSensitivity;
    }

    void PadLookAxisHandler(FCSE_MidHookContext* ctx) {
        if (ctx->edx < kFirstLookAxis || ctx->edx > kLastLookAxis) {
            return;
        }

        if (ctx->xmm1.f32[0] > ctx->xmm2.f32[0]) {
            g_padIsLookDevice = true;
        }
    }

    // Press only, which also excludes the 256-key sweep a lost device produces - its states are all
    // zero, so nothing there reads as pressed.
    void KeyboardHandler(FCSE_MidHookContext* ctx) {
        const uint8_t* keyStates = *reinterpret_cast<uint8_t**>(ctx->esp + 4);
        const int32_t key = *reinterpret_cast<int32_t*>(ctx->esp + 0xC);

        if (keyStates == nullptr || key < 0 || key >= kKeyboardKeyCount) {
            return;
        }

        if (keyStates[key] & kPressedBit) {
            g_padIsLookDevice = false;
        }
    }

    void MouseButtonHandler(FCSE_MidHookContext* ctx) {
        const uint8_t* frameState = *reinterpret_cast<uint8_t**>(ctx->esp + 8);
        const int32_t button = *reinterpret_cast<int32_t*>(ctx->esp + 0xC);

        if (frameState == nullptr || button < 0 || button >= kMouseButtonCount) {
            return;
        }

        if (frameState[kMouseButtons + button] & kPressedBit) {
            g_padIsLookDevice = false;
        }
    }

    void MouseMoveHandler(FCSE_MidHookContext* ctx) {
        const int32_t* accumulator = *reinterpret_cast<int32_t**>(ctx->esp + 8);
        if (accumulator == nullptr) {
            return;
        }

        if (accumulator[0] != 0 || accumulator[1] != 0) {
            g_padIsLookDevice = false;
        }
    }
}

void InstallLookSensitivityHook() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    if (!g_lookHandler) {
        api->Log("look sensitivity: the pawn's look handler was not found in this build - "
                 "sensitivity cannot be changed");
        return;
    }

    api->MidHook(reinterpret_cast<void*>(g_lookHandler.address() + kSensitivityLoaded),
                 &LookSensitivityHandler);

    // Without this the pad never claims the look and both sliders drive the mouse value, which is
    // the stock arrangement rather than a broken one.
    if (!g_padLookAxis) {
        return;
    }

    api->MidHook(reinterpret_cast<void*>(g_padLookAxis.address() + kLookAxisTested),
                 &PadLookAxisHandler);

    if (g_keyboardOnKey) {
        api->MidHook(reinterpret_cast<void*>(g_keyboardOnKey.address()), &KeyboardHandler);
    }

    if (g_mouseOnButton) {
        api->MidHook(reinterpret_cast<void*>(g_mouseOnButton.address()), &MouseButtonHandler);
    }

    if (g_mouseOnMove) {
        api->MidHook(reinterpret_cast<void*>(g_mouseOnMove.address()), &MouseMoveHandler);
    }
}

void __cdecl OnMouseSensitivityChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_mouseSensitivity = static_cast<float>(value->asSlider) / 100.0f;
}

void __cdecl OnControllerSensitivityChanged(const FCSE_SettingValue* value, void* /*userdata*/) {
    g_controllerSensitivity = static_cast<float>(value->asSlider) / 100.0f;
}
