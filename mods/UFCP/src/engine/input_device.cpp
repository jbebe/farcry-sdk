// Which device is actually in the player's hands.
//
// Ported from FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482) - source/input/inputdevice.ixx.
//
// Far Cry 2 shipped on PC with the gamepad a second-class citizen: several behaviours that should
// follow the device in use instead follow whatever the action map last saw, which has already lost
// the distinction. Four driver entry points still know, so the answer is assembled from them:
// CInputDriverGamepad::Poll votes for the pad, and the keyboard's OnKey and the mouse's OnButton
// and OnMove vote against it.
//
// The gamepad vote reads the driver's own XINPUT_STATE at this+0x14 rather than the packet compare
// that follows it, because XInput only bumps the packet number when something changes - a held
// stick would stop voting.
#include "engine/input_device.h"

#include "fcse_api.h"

#include <cstdint>
#include <cstdio>

namespace {
    // XINPUT_STATE at this+0x14: dwPacketNumber, then the XINPUT_GAMEPAD behind it.
    constexpr ptrdiff_t kPadButtons = 0x18;
    constexpr ptrdiff_t kPadLeftTrigger = 0x1A;
    constexpr ptrdiff_t kPadRightTrigger = 0x1B;
    constexpr ptrdiff_t kPadThumbLX = 0x1C;
    constexpr ptrdiff_t kPadThumbLY = 0x1E;
    constexpr ptrdiff_t kPadThumbRX = 0x20;
    constexpr ptrdiff_t kPadThumbRY = 0x22;

    // Standard XInput deadzones. The poll runs every frame, so a stick resting off centre would pin
    // the flag on without them.
    constexpr int32_t kLeftThumbDeadzone = 7849;
    constexpr int32_t kRightThumbDeadzone = 8689;
    constexpr int32_t kTriggerThreshold = 30;

    // Past the XInputGetState failure branch, where the state has just been filled in.
    constexpr ptrdiff_t kPadStateReady = 0x1F;

    // DirectInput reports a byte per key and per button, high bit set while held.
    constexpr uint8_t kPressedBit = 0x80;

    // Button bytes in the mouse driver's frame state, after the x, y and wheel deltas.
    constexpr ptrdiff_t kMouseButtons = 0x0C;
    constexpr int32_t kMouseButtonCount = 8;
    constexpr int32_t kKeyboardKeyCount = 256;

    bool g_padIsActiveDevice = false;

    void (*g_changed)() = nullptr;

    // CInputDriverGamepad::Poll, entered where the state is ready and ESI is the driver.
    FCSE::Relocation<uint8_t*> g_padPoll{FCSE::Pattern(
        "56 8B F1 8B 4E 0C 8B 46 14 57 8D 7E 14 57 51 89 46 24 E8 ?? ?? ?? ?? 85 C0 0F 85")};

    // CInputDriverKeyboard::OnKey(keyStates, sink, keyIndex). The builds diverge at the read of
    // this+8: GOG takes it as a word, Steam as a byte, so whichever resolves is the one that is
    // there.
    FCSE::Relocation<uint8_t*> g_keyboardOnKeyRetail{FCSE::Pattern(
        "8B 44 24 0C 83 EC 30 8D 14 C5 00 00 00 00 2B D0 56 8D 34 91 0F B7 56 08")};
    FCSE::Relocation<uint8_t*> g_keyboardOnKeyUplay{FCSE::Pattern(
        "8B 44 24 0C 83 EC 30 8D 14 C5 00 00 00 00 2B D0 56 8D 34 91 8A 56 08 F6 C2 01")};

    // CInputDriverMouse::OnButton(sink, frameState, buttonIndex, controlId).
    FCSE::Relocation<uint8_t*> g_mouseOnButton{
        FCSE::Pattern("8B 44 24 08 83 EC 30 53 56 57 8B 7C 24 48 8A 5C 38 0C 8B F1 C0 EB 07")};

    // CInputDriverMouse::OnMove, given x and y accumulated over the frame.
    FCSE::Relocation<uint8_t*> g_mouseOnMove{
        FCSE::Pattern("8B 44 24 08 F3 0F 2A 10 F3 0F 2A 58 04 83 EC 14 53 55 8B E9 0F 2E 55 18")};

    void SetPadActiveDevice(bool pad) {
        if (g_padIsActiveDevice == pad) {
            return;
        }

        g_padIsActiveDevice = pad;
        if (g_changed != nullptr) {
            g_changed();
        }
    }

    // 64-bit deliberately: two full-scale axes square to 2,147,352,578, just over INT32_MAX.
    bool ThumbOutsideDeadzone(int16_t x, int16_t y, int32_t deadzone) {
        const int64_t dx = x;
        const int64_t dy = y;
        const int64_t limit = deadzone;
        return dx * dx + dy * dy > limit * limit;
    }

    void PadPollHandler(FCSE_MidHookContext* ctx) {
        const uintptr_t pad = ctx->esi;

        const uint16_t buttons = *reinterpret_cast<uint16_t*>(pad + kPadButtons);
        const uint8_t leftTrigger = *reinterpret_cast<uint8_t*>(pad + kPadLeftTrigger);
        const uint8_t rightTrigger = *reinterpret_cast<uint8_t*>(pad + kPadRightTrigger);
        const int16_t thumbLX = *reinterpret_cast<int16_t*>(pad + kPadThumbLX);
        const int16_t thumbLY = *reinterpret_cast<int16_t*>(pad + kPadThumbLY);
        const int16_t thumbRX = *reinterpret_cast<int16_t*>(pad + kPadThumbRX);
        const int16_t thumbRY = *reinterpret_cast<int16_t*>(pad + kPadThumbRY);

        if (buttons != 0 || leftTrigger > kTriggerThreshold || rightTrigger > kTriggerThreshold ||
            ThumbOutsideDeadzone(thumbLX, thumbLY, kLeftThumbDeadzone) ||
            ThumbOutsideDeadzone(thumbRX, thumbRY, kRightThumbDeadzone)) {
            SetPadActiveDevice(true);
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
            SetPadActiveDevice(false);
        }
    }

    void MouseButtonHandler(FCSE_MidHookContext* ctx) {
        const uint8_t* frameState = *reinterpret_cast<uint8_t**>(ctx->esp + 8);
        const int32_t button = *reinterpret_cast<int32_t*>(ctx->esp + 0xC);

        if (frameState == nullptr || button < 0 || button >= kMouseButtonCount) {
            return;
        }

        if (frameState[kMouseButtons + button] & kPressedBit) {
            SetPadActiveDevice(false);
        }
    }

    void MouseMoveHandler(FCSE_MidHookContext* ctx) {
        const int32_t* accumulator = *reinterpret_cast<int32_t**>(ctx->esp + 8);
        if (accumulator == nullptr) {
            return;
        }

        if (accumulator[0] != 0 || accumulator[1] != 0) {
            SetPadActiveDevice(false);
        }
    }
}

namespace UFCP {

bool IsPadActiveDevice() {
    return g_padIsActiveDevice;
}

void OnInputDeviceChanged(void (*callback)()) {
    g_changed = callback;
}

void InstallInputDeviceTracking() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    int installed = 0;

    if (g_padPoll) {
        installed += api->MidHook(reinterpret_cast<void*>(g_padPoll.address() + kPadStateReady),
                                  &PadPollHandler);
    }

    const uintptr_t keyboard = g_keyboardOnKeyRetail  ? g_keyboardOnKeyRetail.address()
                               : g_keyboardOnKeyUplay ? g_keyboardOnKeyUplay.address()
                                                      : 0;
    if (keyboard != 0) {
        installed += api->MidHook(reinterpret_cast<void*>(keyboard), &KeyboardHandler);
    }

    if (g_mouseOnButton) {
        installed +=
            api->MidHook(reinterpret_cast<void*>(g_mouseOnButton.address()), &MouseButtonHandler);
    }

    if (g_mouseOnMove) {
        installed +=
            api->MidHook(reinterpret_cast<void*>(g_mouseOnMove.address()), &MouseMoveHandler);
    }

    // Every feature that asks which device is in use degrades to "keyboard and mouse" without this,
    // which is the stock behaviour, so a partial install is worth reporting but not worth refusing.
    char line[128];
    std::snprintf(line, sizeof(line), "input device: tracking %d of 4 drivers", installed);
    api->Log(line);
}

}
