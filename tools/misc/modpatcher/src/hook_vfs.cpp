#include "hook_vfs.h"
#include "dunia.h"
#include "log.h"

#include <MinHook.h>
#include <windows.h>

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>

namespace LooseMods {

namespace {

// --- Facts confirmed against the live Ghidra project / disassembly, see
// reverse/dunia/archive_loading.md. All addresses are RVAs relative to Dunia.dll's own
// preferred image base (0x10000000, confirmed via list_segments), not absolute VAs - the
// module can only be a fixed-base 2008-era image in practice, but we compute from the actual
// load address regardless.
constexpr uintptr_t kVfsResolvePathRva = 0x002358a0;

// The real function's own absolute-path branch copies into a fixed 260-byte (MAX_PATH) stack
// buffer with no bounds check beyond that size (confirmed in disassembly around 0x10235aa8-
// 0x10235abc). Our rewritten path must fit inside that, including the null terminator.
constexpr size_t kMaxRewrittenPathLen = MAX_PATH - 1;

// VFS_ResolvePath(this, relativePath, modeFlags, forceFlag) - __thiscall, this in ECX.
// Confirmed via disassembly: function ends in `RET 0xc`, i.e. exactly 3 stack args beyond `this`.
using VfsResolvePathFn = void* (__thiscall*)(void* thisPtr, char* relativePath,
                                              unsigned int modeFlags, char forceFlag);

VfsResolvePathFn g_original = nullptr;
void* g_hookTarget = nullptr;

// Mirrors Dunia.dll's Path_IsAbsolute (0x10231510): true iff the path contains ':' (drive
// letter) or starts with "\\\\" (UNC). We don't call the real function - this hook runs before
// the real one does any work, so we just need the same classification to decide whether an
// override is even meaningful (an already-absolute path is already going straight to disk).
bool IsAbsolutePath(const char* path) {
    if (!path) {
        return false;
    }
    if (strchr(path, ':') != nullptr) {
        return true;
    }
    return path[0] == '\\' && path[1] == '\\';
}

std::wstring NarrowToWide(const char* narrow) {
    if (!narrow || !*narrow) {
        return std::wstring();
    }
    int len = MultiByteToWideChar(CP_ACP, 0, narrow, -1, nullptr, 0);
    if (len <= 0) {
        return std::wstring();
    }
    std::wstring wide(static_cast<size_t>(len) - 1, L'\0');
    MultiByteToWideChar(CP_ACP, 0, narrow, -1, wide.data(), len);
    return wide;
}

// Builds "<installRoot>Data_Win32\Loose\<relativePath>" into outBuf (kMaxRewrittenPathLen+1
// bytes), normalizing '/' to '\\' to match the mixed conventions Dunia.dll itself uses (e.g.
// "worlds/world1/..." vs "\entitylibrary.fcb"). Returns false (and does not touch outBuf) if the
// result wouldn't fit in the real function's 260-byte stack buffer.
//
// installRoot is the game's install directory (parent of bin\, where this DLL actually lives) -
// this matches Data_Win32\Loose\ sitting right next to the real patch.dat/common.dat/worlds\ a
// modder already knows from unpacking archives with Gibbed's tools, not some tool-invented
// location next to the DLL itself. Deliberately just "go up one directory from wherever this DLL
// is," not a hardcoded "bin" literal - robust to wherever it's actually installed.
bool BuildLoosePath(const char* relativePath, char* outBuf, size_t outBufSize) {
    // Log::ModuleDirectory() is wide (from GetModuleFileNameW); the game's own paths are narrow
    // (local codepage / ASCII in practice, per Dunia.dll's own char* archive-name strings).
    // Convert and compute the install root once here rather than every call.
    static std::string installRootNarrow = [] {
        const std::wstring& moduleDirWide = Log::ModuleDirectory();
        if (moduleDirWide.empty()) {
            return std::string();
        }
        int len = WideCharToMultiByte(CP_ACP, 0, moduleDirWide.c_str(), -1, nullptr, 0, nullptr,
                                       nullptr);
        if (len <= 0) {
            return std::string();
        }
        std::string moduleDir(static_cast<size_t>(len) - 1, '\0');
        WideCharToMultiByte(CP_ACP, 0, moduleDirWide.c_str(), -1, moduleDir.data(), len, nullptr,
                             nullptr);

        // moduleDir ends in "...\bin\" (trailing slash included) - strip it, then strip back to
        // the slash before the last component to get "...\" (the install root, trailing slash
        // included).
        if (!moduleDir.empty() && moduleDir.back() == '\\') {
            moduleDir.pop_back();
        }
        size_t slash = moduleDir.find_last_of('\\');
        if (slash == std::string::npos) {
            return std::string();
        }
        return moduleDir.substr(0, slash + 1);
    }();

    if (installRootNarrow.empty()) {
        return false;
    }

    std::string candidate = installRootNarrow + "Data_Win32\\Loose\\" + relativePath;
    for (char& c : candidate) {
        if (c == '/') {
            c = '\\';
        }
    }

    if (candidate.size() > kMaxRewrittenPathLen) {
        return false;
    }

    strcpy_s(outBuf, outBufSize, candidate.c_str());
    return true;
}

// MSVC rejects an explicit __thiscall on a free function (C3865 - "can only be used on native
// member functions"), so we can't literally match VfsResolvePathFn's calling convention keyword
// for keyword. The standard workaround: __fastcall passes its first two arguments in
// ECX/EDX, and the real function only ever puts `this` in ECX with nothing meaningful in EDX
// (confirmed by the disassembly - no callee ever relies on EDX holding anything at entry), so a
// dummy unused EDX parameter here reproduces the exact same incoming register/stack contract.
void* __fastcall Detour_VfsResolvePath(void* thisPtr, void* /*edx, unused*/, char* relativePath,
                                        unsigned int modeFlags, char forceFlag) {
    // 1. Recursive re-entries (the 0x20 cache wrapper and 0x40 buffered wrapper both call back
    //    into this same function internally, confirmed via disassembly at 0x1023590c/0x10235986)
    //    are not real path-lookup requests in the 0x20 case; pass those through untouched.
    //    The 0x40 branch's inner recursive call (mode=2, no 0x60 bits) DOES reach here again with
    //    the real path and IS worth checking - see archive_loading.md / the plan for why that's
    //    correct, not a double-processing bug.
    if (modeFlags & 0x20) {
        return g_original(thisPtr, relativePath, modeFlags, forceFlag);
    }

    if (!relativePath || !*relativePath || IsAbsolutePath(relativePath)) {
        return g_original(thisPtr, relativePath, modeFlags, forceFlag);
    }

    char loosePath[MAX_PATH];
    if (!BuildLoosePath(relativePath, loosePath, sizeof(loosePath))) {
        Log::Warn(L"[VFS] loose path too long, skipping override for: " +
                   NarrowToWide(relativePath));
        return g_original(thisPtr, relativePath, modeFlags, forceFlag);
    }

    DWORD attrs = GetFileAttributesA(loosePath);
    bool found = attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY);

    if (found) {
        Log::Info(L"[VFS] override HIT  " + NarrowToWide(relativePath) + L" -> " +
                   NarrowToWide(loosePath));
        return g_original(thisPtr, loosePath, modeFlags, forceFlag);
    }

    Log::Info(L"[VFS] passthrough   " + NarrowToWide(relativePath));
    return g_original(thisPtr, relativePath, modeFlags, forceFlag);
}

} // namespace

bool InstallVfsHook() {
    uintptr_t base = 0;
    if (!GetVerifiedDuniaBase(base)) {
        return false; // GetVerifiedDuniaBase already logged the reason
    }

    g_hookTarget = reinterpret_cast<void*>(base + kVfsResolvePathRva);

    // MinHook may already be initialized by another hook installer - that's expected and fine.
    MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED) {
        Log::Error(L"MH_Initialize failed.");
        return false;
    }

    if (MH_CreateHook(g_hookTarget, reinterpret_cast<void*>(&Detour_VfsResolvePath),
                       reinterpret_cast<void**>(&g_original)) != MH_OK) {
        Log::Error(L"MH_CreateHook on VFS_ResolvePath failed.");
        return false;
    }

    if (MH_EnableHook(g_hookTarget) != MH_OK) {
        Log::Error(L"MH_EnableHook on VFS_ResolvePath failed.");
        return false;
    }

    Log::Info(L"VFS_ResolvePath hook installed.");
    return true;
}

void RemoveVfsHook() {
    if (g_hookTarget) {
        MH_DisableHook(g_hookTarget);
        MH_RemoveHook(g_hookTarget);
        g_hookTarget = nullptr;
    }
    MH_Uninitialize();
}

} // namespace LooseMods
