#include "proxy_exports.h"
#include "log.h"

#include <windows.h>
#include <string>

// MSVC's linker cannot create true cross-DLL PE export forwarders from source (no /EXPORT or
// .def syntax reliably does it - both were tried and both fail with LNK2001, the right-hand side
// always has to resolve to a real symbol in our own objects). The standard, actually-working
// technique: resolve each real export via GetProcAddress into a global function pointer, then
// expose a naked asm thunk per export that JMPs (not CALLs) indirectly through that pointer. A
// JMP preserves the original caller's stack frame exactly, so the real function sees precisely
// the arguments the caller set up and RETs straight back to the caller - this works regardless
// of each export's actual calling convention/signature, so we don't need to get any of the 6
// (some of which, like GetdfDIJoystick, aren't part of the documented public API) exactly right.

namespace {
    FARPROC g_realDirectInput8Create = nullptr;
    FARPROC g_realDllCanUnloadNow = nullptr;
    FARPROC g_realDllGetClassObject = nullptr;
    FARPROC g_realDllRegisterServer = nullptr;
    FARPROC g_realDllUnregisterServer = nullptr;
    FARPROC g_realGetdfDIJoystick = nullptr;
}

namespace LooseMods {

bool LoadRealDinput8() {
    // Resolve an EXPLICIT, ABSOLUTE path to the real system dinput8.dll rather than a bare
    // LoadLibraryW(L"dinput8.dll") - a bare name would search our own directory first (standard
    // DLL search order) and just re-resolve to ourselves, since we share that exact filename.
    // GetSystemDirectoryW, called from inside this 32-bit DLL, is automatically WOW64-redirected
    // to SysWOW64 on 64-bit Windows, so this always resolves to the correct-architecture copy
    // without us needing to reason about host OS bitness at all. No bundled/renamed copy needed -
    // same technique ENB/SKSE-style loaders use for their proxy DLLs.
    wchar_t sysDir[MAX_PATH];
    UINT len = GetSystemDirectoryW(sysDir, MAX_PATH);
    if (len == 0 || len >= MAX_PATH) {
        Log::Error(L"LoadRealDinput8: GetSystemDirectoryW failed.");
        return false;
    }

    std::wstring realPath = std::wstring(sysDir, len) + L"\\dinput8.dll";
    HMODULE real = LoadLibraryW(realPath.c_str());
    if (!real) {
        Log::Error(L"LoadRealDinput8: LoadLibraryW on the real system dinput8.dll failed: " +
                    realPath);
        return false;
    }

    g_realDirectInput8Create = GetProcAddress(real, "DirectInput8Create");
    g_realDllCanUnloadNow = GetProcAddress(real, "DllCanUnloadNow");
    g_realDllGetClassObject = GetProcAddress(real, "DllGetClassObject");
    g_realDllRegisterServer = GetProcAddress(real, "DllRegisterServer");
    g_realDllUnregisterServer = GetProcAddress(real, "DllUnregisterServer");
    g_realGetdfDIJoystick = GetProcAddress(real, "GetdfDIJoystick");

    if (!g_realDirectInput8Create || !g_realDllCanUnloadNow || !g_realDllGetClassObject ||
        !g_realDllRegisterServer || !g_realDllUnregisterServer || !g_realGetdfDIJoystick) {
        Log::Error(L"LoadRealDinput8: the real system dinput8.dll is missing one or more "
                    L"expected exports.");
        return false;
    }

    Log::Info(L"Real system dinput8.dll loaded and all 6 exports resolved: " + realPath);
    return true;
}

} // namespace LooseMods

extern "C" {

// Aliased to the public export names in CMakeLists.txt via /EXPORT:Public=ThisName (using the
// undecorated name - the linker resolves the actual C-decorated symbol itself).

__declspec(naked) void __cdecl DirectInput8Create_Thunk() {
    __asm { jmp[g_realDirectInput8Create] }
}

__declspec(naked) void __cdecl DllCanUnloadNow_Thunk() {
    __asm { jmp[g_realDllCanUnloadNow] }
}

__declspec(naked) void __cdecl DllGetClassObject_Thunk() {
    __asm { jmp[g_realDllGetClassObject] }
}

__declspec(naked) void __cdecl DllRegisterServer_Thunk() {
    __asm { jmp[g_realDllRegisterServer] }
}

__declspec(naked) void __cdecl DllUnregisterServer_Thunk() {
    __asm { jmp[g_realDllUnregisterServer] }
}

__declspec(naked) void __cdecl GetdfDIJoystick_Thunk() {
    __asm { jmp[g_realGetdfDIJoystick] }
}

} // extern "C"
