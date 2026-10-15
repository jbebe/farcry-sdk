// Many cores: the game closes at the splash screen on a machine with 32 or more logical processors.
//
// The splash shows for a few seconds and the process is gone, with no error. bin\systemdetection.dll,
// the hardware probe Dunia loads at startup, walks the processors one affinity bit at a time and
// records each one's APIC id into a 256-byte stack array. The walk ends when the bit passes the
// system mask, or when the bit shifts out to zero:
//
//     83 C5 01         add  ebp, 1            ; next processor
//     BE 01 00 00 00   mov  esi, 1
//     8B CD            mov  ecx, ebp
//     D3 E6            shl  esi, cl           ; bit = 1 << index, but x86 shifts by index & 31
//     85 F6            test esi, esi
//     0F 85 ..         jnz  loop
//
// The shift wraps at 32, so the bit is never zero, and a mask of 0xFFFFFFFF has no bit above it -
// with all 32 processors the walk never ends and writes up the stack until it faults at the top.
// RVA 0xC594 in both shipped copies, whose code is identical. The patch keeps the shift and replaces
// the zero test with `cmp ebp, 32`, so every processor is still probed and the walk stops after 32.
#include "fcse_api.h"

#include <windows.h>

#include <cstdint>
#include <cstring>

namespace {
    // The loop's tail, from `add ebp, 1` through the opcode of its `jnz`.
    constexpr uint8_t kLoopTail[] = {0x83, 0xC5, 0x01, 0xBE, 0x01, 0x00, 0x00, 0x00,
                                     0x8B, 0xCD, 0xD3, 0xE6, 0x85, 0xF6, 0x0F, 0x85};

    // From `mov esi, 1` to the `jnz`: xor esi, esi / inc esi / mov ecx, ebp / shl esi, cl /
    // cmp ebp, 32 / nop. The `jnz` then leaves the loop once the index reaches 32.
    constexpr ptrdiff_t kPatchOffset = 3;
    constexpr uint8_t kBoundedTail[] = {0x33, 0xF6, 0x46, 0x8B, 0xCD, 0xD3,
                                        0xE6, 0x83, 0xFD, 0x20, 0x90};

    bool g_patched = false;

    struct LdrUnicodeString {
        USHORT Length;
        USHORT MaximumLength;
        wchar_t* Buffer;
    };

    struct LdrDllLoadedNotificationData {
        ULONG Flags;
        const LdrUnicodeString* FullDllName;
        const LdrUnicodeString* BaseDllName;
        void* DllBase;
        ULONG SizeOfImage;
    };

    constexpr ULONG kLdrDllNotificationReasonLoaded = 1;

    using LdrNotificationFn = void(__stdcall*)(ULONG, const LdrDllLoadedNotificationData*, void*);
    using LdrRegisterFn = LONG(__stdcall*)(ULONG, LdrNotificationFn, void*, void**);

    // The loader hands over a counted string, which need not be terminated.
    bool IsSystemDetection(const LdrUnicodeString* name) {
        constexpr wchar_t kName[] = L"systemdetection.dll";
        constexpr size_t kLength = (sizeof(kName) / sizeof(kName[0])) - 1;

        return name != nullptr && name->Buffer != nullptr &&
               name->Length == kLength * sizeof(wchar_t) &&
               _wcsnicmp(name->Buffer, kName, kLength) == 0;
    }

    // The single match of kLoopTail in the image, or nullptr if there is none or more than one.
    uint8_t* FindLoopTail(uint8_t* image, size_t size) {
        uint8_t* found = nullptr;
        for (size_t i = 0; i + sizeof(kLoopTail) <= size; ++i) {
            if (std::memcmp(image + i, kLoopTail, sizeof(kLoopTail)) == 0) {
                if (found != nullptr) {
                    return nullptr;
                }
                found = image + i;
            }
        }
        return found;
    }

    // Runs under the loader lock, before the probe's DllMain.
    void __stdcall OnDllLoaded(ULONG reason, const LdrDllLoadedNotificationData* data, void*) {
        if (reason != kLdrDllNotificationReasonLoaded || g_patched ||
            !IsSystemDetection(data->BaseDllName)) {
            return;
        }

        const FCSE_PluginAPI* api = FCSE::ApiPointer();
        g_patched = true;

        uint8_t* tail = FindLoopTail(static_cast<uint8_t*>(data->DllBase), data->SizeOfImage);
        if (tail == nullptr) {
            api->Log("many cores: the hardware probe's processor loop was not found - not fixed");
            return;
        }

        if (api->Patch(tail + kPatchOffset, kBoundedTail, sizeof(kBoundedTail))) {
            api->Log("many cores: the hardware probe stops after 32 processors");
        }
    }
}

// Dunia loads the probe after FCSE_Load, so the patch waits for the DLL to arrive.
void ApplyManyCoresFix() {
    const FCSE_PluginAPI* api = FCSE::ApiPointer();

    const HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
    const auto registerNotification = reinterpret_cast<LdrRegisterFn>(
        ntdll != nullptr ? GetProcAddress(ntdll, "LdrRegisterDllNotification") : nullptr);

    void* cookie = nullptr;
    if (registerNotification == nullptr ||
        registerNotification(0, &OnDllLoaded, nullptr, &cookie) != 0) {
        api->Log("many cores: could not watch for the hardware probe - not fixed");
        return;
    }

    api->Log("many cores: watching for the hardware probe");
}
