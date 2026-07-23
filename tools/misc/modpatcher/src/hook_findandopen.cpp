#include "hook_findandopen.h"
#include "dunia.h"
#include "log.h"
#include "loose_index.h"

#include <MinHook.h>
#include <windows.h>

#include <cstdint>
#include <string>

namespace LooseMods {

namespace {

// --- All addresses are RVAs relative to Dunia.dll's preferred image base (0x10000000), resolved
// against the actual load base at install time. Confirmed via the live Ghidra project /
// disassembly this session - see reverse/dunia/archive_loading.md.

// ArchiveEntry_FindAndOpen(this=searchPathEntry, uint32_t* hashPtr, uint mode). __thiscall,
// RET 0x8 (this in ECX + 2 stack args). Reads *hashPtr = the 32-bit path hash and binary-searches
// this entry's .fat index for it. 5 callers, every hash-based open funnels through here.
constexpr uintptr_t kFindAndOpenRva = 0x00249070;

// VFS_OpenFileRaw(char* path, uint mode) - __cdecl. Maps mode onto CreateFileW flags and opens a
// raw HANDLE. This is what LevelAsset_OpenStream's own raw-disk branch calls.
constexpr uintptr_t kVfsOpenFileRawRva = 0x00231ae0;

// Engine allocator FUN_10228f30(size, 0) - __cdecl. Used so the stream object we hand back is
// allocated exactly like the engine's own and can be freed by it normally.
constexpr uintptr_t kEngineAllocRva = 0x00228f30;

// Stream-over-handle constructor FUN_1024b310(this=obj, HANDLE, byte flag) - __thiscall. Wraps a
// raw HANDLE in the same stream object (vtable PTR_FUN_10e2e334) that LevelAsset_OpenStream's
// raw-disk branch returns - proving it's interface-compatible with the archive sub-stream the
// callers otherwise expect.
constexpr uintptr_t kStreamCtorRva = 0x0024b310;

// Open the loose file with GENERIC_READ | OPEN_EXISTING | (NORMAL | SEQUENTIAL_SCAN), buffered.
// This is VFS_OpenFileRaw's mode 0x10 branch (confirmed in its disassembly). Deliberately NOT the
// incoming FindAndOpen `mode` - that governs archive sub-stream positioning, not CreateFileW
// semantics - and deliberately not mode 0x8, whose FILE_FLAG_NO_BUFFERING would force
// sector-aligned reads that an arbitrary loose file can't guarantee.
constexpr uint32_t kLooseReadMode = 0x10;

// Passthrough logging. Unlike VFS_ResolvePath (~130 calls across a whole boot), this function is
// the innermost archive lookup - it runs once per mounted archive per request, so a real boot
// drives it tens to hundreds of thousands of times. Logging every call synchronously would bloat
// the log and stutter the game, so we only tick a running total every kPassthroughSummaryEvery
// calls - enough of a heartbeat to prove the hook is live without flooding the log.
constexpr long kPassthroughSummaryEvery = 200;

volatile long g_passthroughCount = 0;

using FindAndOpenFn = void*(__thiscall*)(void* thisEntry, uint32_t* hashPtr, uint32_t mode);
using VfsOpenFileRawFn = void*(__cdecl*)(const char* path, uint32_t mode);
using EngineAllocFn = void*(__cdecl*)(size_t size, int flag);
using StreamCtorFn = void*(__thiscall*)(void* obj, void* handle, int flag);

FindAndOpenFn g_originalFindAndOpen = nullptr;
VfsOpenFileRawFn g_vfsOpenFileRaw = nullptr;
EngineAllocFn g_engineAlloc = nullptr;
StreamCtorFn g_streamCtor = nullptr;
void* g_hookTarget = nullptr;

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

// Reproduces LevelAsset_OpenStream's raw-disk construction (0x107e070c-0x107e0735): open the file
// via the engine's own opener, allocate the stream object with the engine's own allocator, and
// run the engine's own stream-over-handle constructor. Returns a stream object interface-identical
// to what ArchiveEntry_FindAndOpen would have returned, or nullptr on any failure (caller then
// falls through to the real function).
void* OpenLooseStream(const char* absPath) {
    void* handle = g_vfsOpenFileRaw(absPath, kLooseReadMode);
    if (handle == nullptr || handle == INVALID_HANDLE_VALUE) {
        return nullptr;
    }
    void* obj = g_engineAlloc(0xc, 0);
    if (obj == nullptr) {
        CloseHandle(handle);
        return nullptr;
    }
    g_streamCtor(obj, handle, 0);
    return obj;
}

// __fastcall + dummy EDX reproduces the incoming __thiscall register/stack contract (this in ECX,
// nothing meaningful in EDX) - same technique as hook_vfs.cpp's detour, since MSVC forbids an
// explicit __thiscall on a free function.
void* __fastcall Detour_FindAndOpen(void* thisEntry, void* /*edx, unused*/, uint32_t* hashPtr,
                                     uint32_t mode) {
    if (hashPtr) {
        const std::string* loose = LookupLooseByHash(*hashPtr);
        if (loose) {
            void* stream = OpenLooseStream(loose->c_str());
            if (stream) {
                wchar_t hex[16];
                swprintf_s(hex, L"0x%08X", *hashPtr);
                Log::Info(std::wstring(L"[HASH] override HIT  ") + hex + L" (mode " +
                           std::to_wstring(mode) + L") -> " + NarrowToWide(loose->c_str()));
                return stream;
            }
            // Indexed but couldn't be opened - fall through to stock behavior rather than fail the
            // load (never worse than an un-hooked engine).
            Log::Warn(std::wstring(L"[HASH] loose file failed to open, using archive: ") +
                       NarrowToWide(loose->c_str()));
        }
    }

    // Not overridden - tick a heartbeat total every kPassthroughSummaryEvery calls. The engine
    // calls us from several threads, so the counter is interlocked; the log only needs to be
    // roughly ordered, not exact.
    long n = InterlockedIncrement(&g_passthroughCount);
    if (n % kPassthroughSummaryEvery == 0) {
        Log::Info(L"[HASH] passthrough total: " + std::to_wstring(n) + L" lookups (no loose match)");
    }
    return g_originalFindAndOpen(thisEntry, hashPtr, mode);
}

} // namespace

bool InstallFindAndOpenHook() {
    uintptr_t base = 0;
    if (!GetVerifiedDuniaBase(base)) {
        return false; // GetVerifiedDuniaBase already logged the reason
    }

    g_vfsOpenFileRaw = reinterpret_cast<VfsOpenFileRawFn>(base + kVfsOpenFileRawRva);
    g_engineAlloc = reinterpret_cast<EngineAllocFn>(base + kEngineAllocRva);
    g_streamCtor = reinterpret_cast<StreamCtorFn>(base + kStreamCtorRva);
    g_hookTarget = reinterpret_cast<void*>(base + kFindAndOpenRva);

    // MinHook may already be initialized by InstallVfsHook - that's expected and fine.
    MH_STATUS init = MH_Initialize();
    if (init != MH_OK && init != MH_ERROR_ALREADY_INITIALIZED) {
        Log::Error(L"MH_Initialize failed (FindAndOpen hook).");
        return false;
    }

    if (MH_CreateHook(g_hookTarget, reinterpret_cast<void*>(&Detour_FindAndOpen),
                       reinterpret_cast<void**>(&g_originalFindAndOpen)) != MH_OK) {
        Log::Error(L"MH_CreateHook on ArchiveEntry_FindAndOpen failed.");
        return false;
    }

    if (MH_EnableHook(g_hookTarget) != MH_OK) {
        Log::Error(L"MH_EnableHook on ArchiveEntry_FindAndOpen failed.");
        return false;
    }

    Log::Info(L"ArchiveEntry_FindAndOpen hook installed.");
    return true;
}

void RemoveFindAndOpenHook() {
    if (g_hookTarget) {
        MH_DisableHook(g_hookTarget);
        MH_RemoveHook(g_hookTarget);
        g_hookTarget = nullptr;

        Log::Info(L"[HASH] final passthrough total: " + std::to_wstring(g_passthroughCount) +
                   L" lookup(s).");
    }
    // MH_Uninitialize is left to RemoveVfsHook so it runs exactly once on detach.
}

} // namespace LooseMods
