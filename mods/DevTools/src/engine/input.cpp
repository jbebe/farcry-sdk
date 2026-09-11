// Two seams, because the game uses two. Window messages are taken by subclassing the window;
// everything that moves the player comes through DirectInput instead, where a message hook has no
// reach, so the device reads are detoured as well and answer with an idle device.
#include "engine/input.h"

#include "fcse_api.h"

#include <atomic>
#include <cstring>

// Before dinput.h, which otherwise picks a version for us - and the vtable this file indexes into
// is the one that choice decides.
#define DIRECTINPUT_VERSION 0x0800
#include <dinput.h>

namespace {
    // IDirectInputDevice8A's vtable, as declared in dinput.h.
    constexpr size_t kGetDeviceStateSlot = 9;
    constexpr size_t kGetDeviceDataSlot = 10;

    using GetDeviceStateFn = HRESULT(__stdcall*)(IDirectInputDevice8A* device, DWORD bytes,
                                                 void* data);
    using GetDeviceDataFn = HRESULT(__stdcall*)(IDirectInputDevice8A* device, DWORD objectBytes,
                                                DIDEVICEOBJECTDATA* data, DWORD* count, DWORD flags);

    GetDeviceStateFn g_originalGetDeviceState = nullptr;
    GetDeviceDataFn g_originalGetDeviceData = nullptr;

    WNDPROC g_originalWndProc = nullptr;
    DevTools::Input::MessageFn g_onMessage = nullptr;

    // Written from the thread that pumps messages, read from every DirectInput read the game makes,
    // on whichever thread it makes it.
    std::atomic<bool> g_captured{false};

    // Wheel travel the game's mouse reads carried while captured, until the overlay polls it.
    std::atomic<long> g_wheel{0};

    // A keyboard reports its 7 key at the offset a mouse reports the wheel on.
    bool IsMouse(IDirectInputDevice8A* device) {
        DIDEVCAPS caps{sizeof(caps)};
        return SUCCEEDED(device->GetCapabilities(&caps)) &&
               GET_DIDEVICE_TYPE(caps.dwDevType) == DI8DEVTYPE_MOUSE;
    }

    HRESULT __stdcall GetDeviceStateDetour(IDirectInputDevice8A* device, DWORD bytes, void* data) {
        if (g_captured) {
            // An idle device rather than a refused read, which the engine would take for a lost one.
            std::memset(data, 0, bytes);
            return DI_OK;
        }
        return g_originalGetDeviceState(device, bytes, data);
    }

    HRESULT __stdcall GetDeviceDataDetour(IDirectInputDevice8A* device, DWORD objectBytes,
                                          DIDEVICEOBJECTDATA* data, DWORD* count, DWORD flags) {
        HRESULT read = g_originalGetDeviceData(device, objectBytes, data, count, flags);
        if (!g_captured || count == nullptr) {
            return read;
        }

        if (SUCCEEDED(read) && data != nullptr && IsMouse(device)) {
            for (DWORD event = 0; event < *count; ++event) {
                if (data[event].dwOfs == DIMOFS_Z) {
                    g_wheel += static_cast<LONG>(data[event].dwData);
                }
            }
        }
        // Drained and then reported as empty: left unread, the events pile up and arrive together
        // the moment the overlay closes.
        *count = 0;
        return read;
    }

    LRESULT __stdcall WndProcDetour(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
        if (g_onMessage(window, message, wparam, lparam)) {
            return TRUE;
        }
        // Null only in the instants around the subclassing itself, when the game's own procedure is
        // not recorded yet and a message can already arrive.
        if (g_originalWndProc == nullptr) {
            return DefWindowProcA(window, message, wparam, lparam);
        }
        return CallWindowProcA(g_originalWndProc, window, message, wparam, lparam);
    }

    // Builds a keyboard device only to read its vtable, then lets it go.
    bool ReadDeviceVtable(void** outGetState, void** outGetData) {
        IDirectInput8A* input = nullptr;
        if (FAILED(DirectInput8Create(GetModuleHandleA(nullptr), DIRECTINPUT_VERSION,
                                      IID_IDirectInput8A, reinterpret_cast<void**>(&input),
                                      nullptr))) {
            return false;
        }

        IDirectInputDevice8A* keyboard = nullptr;
        if (FAILED(input->CreateDevice(GUID_SysKeyboard, &keyboard, nullptr))) {
            input->Release();
            return false;
        }

        void** vtable = *reinterpret_cast<void***>(keyboard);
        *outGetState = vtable[kGetDeviceStateSlot];
        *outGetData = vtable[kGetDeviceDataSlot];

        keyboard->Release();
        input->Release();
        return true;
    }
}

namespace DevTools::Input {

bool Install(HWND window, MessageFn onMessage) {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    g_onMessage = onMessage;

    void* getState = nullptr;
    void* getData = nullptr;
    const bool taken =
        ReadDeviceVtable(&getState, &getData) &&
        api->Hook(getState, reinterpret_cast<void*>(&GetDeviceStateDetour),
                  reinterpret_cast<void**>(&g_originalGetDeviceState)) &&
        api->Hook(getData, reinterpret_cast<void*>(&GetDeviceDataDetour),
                  reinterpret_cast<void**>(&g_originalGetDeviceData));
    if (!taken) {
        api->Log("input: DirectInput could not be taken over - the game keeps reading the mouse "
                 "while the overlay is up");
    }

    // Recorded before the swap, not from its return value: the moment the swap lands, a message can
    // arrive on the game's own thread and run the detour.
    g_originalWndProc =
        reinterpret_cast<WNDPROC>(GetWindowLongPtrA(window, GWLP_WNDPROC));
    if (SetWindowLongPtrA(window, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&WndProcDetour)) == 0) {
        api->Log("input: the game's window would not take a message hook - the overlay cannot be "
                 "typed into");
        return false;
    }

    api->Log("input: watching the game's window and its DirectInput devices");
    return true;
}

void SetCaptured(bool captured) {
    g_captured = captured;

    // The game confines the cursor to its window while it has the mouse. Letting it go is what
    // makes the pointer reachable again; the game re-applies its own clip when it next looks.
    if (captured) {
        ClipCursor(nullptr);
    }
}

bool IsCaptured() { return g_captured; }

RawKeys PollRawKeys() {
    auto down = [](int key) { return (GetAsyncKeyState(key) & 0x8000) != 0; };
    return {{down(VK_LBUTTON), down(VK_RBUTTON), down(VK_MBUTTON)},
            down(VK_CONTROL),
            down(VK_SHIFT),
            down(VK_MENU),
            g_wheel.exchange(0)};
}

}
