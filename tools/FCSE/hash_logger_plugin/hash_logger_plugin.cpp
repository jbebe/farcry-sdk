// hash_logger_plugin - a one-off diagnostic plugin, not part of FCSE's normal feature set.
//
// Hooks two separate CRC-32 implementations in Dunia.dll:
//   - CRC32_Hash (0x10229400) - the native, engine-wide utility (shared 256-entry table
//     DAT_10f95388). Confirmed safe to hook with per-call file I/O (logged ~1.7M calls over a full
//     boot-to-menu session with no issue) - covers native class/page hashing, not Magma widget
//     class names.
//   - FUN_10aa7150 (found live in Ghidra by inspection) - magma::Id::Hash's real Dunia.dll
//     equivalent (byte-for-byte the same algorithm as FarCry2_server's decompiled magma::Id::Hash
//     @ 0xa0782a0) - what actually computes Magma widget class hashes for the .mgb type table.
//
// magma::Id::Hash turned out to be unsafe to log unconditionally: a version that only ever
// *reads* (compares against a target hash, no file I/O unless matched) ran a full session fine; a
// version that additionally logged the first ~30 calls unconditionally crashed after exactly one
// successful log line ("[call #0] 4B4B79CD CActionSignalBase" - a real Magma class, confirming the
// hook works). A CRITICAL_SECTION around all file I/O (in case of unsynchronized concurrent
// access) made zero difference - identical crash point, byte-for-byte same CRC32_Hash call count
// both times - which actually rules out a race condition (races aren't this deterministic) as well
// as the concurrency theory. The real mechanism was never pinned down. Given that, this version
// goes back to the one proven-stable shape: only ever touch anything beyond the trampoline call on
// the rare/one-time event of an actual 0x86F001E3 match - confirmed to run a full session
// (including reaching Options) with zero crashes.
//
// The magma::Id::Hash hook above has since run across multiple full sessions (millions of
// CRC32_Hash calls, 2580+ real Id::Hash inputs observed live) and never once matched 0x86F001E3 -
// strong evidence the class behind that hash never actually calls Id::Hash at runtime at all (a
// hardcoded/precomputed hash registered some other way - see docs/docs/file-formats/mgb.md's Open
// Question 1(b)). This session (2026-07-31) adds a third hook, on
// magma::objecttypemanager::Register (0x10a982b0) instead: it fires once per registered class
// regardless of whether Id::Hash is ever separately called for it, receiving the class's real
// ObjectTypeInfo* directly. The class name is extracted via the same double-indirected vtable call
// magma::objecttypemanager::Initialize itself uses before hashing (vtable slot +4, thiscall, zero
// args, returns a descriptor whose first field is the name char*) - confirmed via disassembly of
// Initialize (0x10a98ad0), not guessed. The hash is computed locally (a self-contained CRC32-IEEE
// implementation, no call into the game's own Id::Hash) so this hook never touches an unconfirmed
// runtime dependency. Follows the exact same "match-only" safety shape as the Id::Hash hook above,
// given the crash risk demonstrated there was never explained and might not be specific to that one
// function.
//
// Purpose: find which literal string hashes to 0x86F001E3 - a Magma widget class blocking ~13/21
// shipped .mgb files that JackAll's MgbTypeTable can't resolve (see docs/docs/file-formats/mgb.md's
// "Unknowns" and the FCSE Mods-menu plan file).
#include "../include/plugin_api.h"

#include <cstdio>
#include <cstdint>
#include <windows.h>

namespace {
    const FCSE_PluginAPI* g_api = nullptr;
    FILE* g_crc32LogFile = nullptr;

    // Confirmed via live testing: with the target-hash-match version (no file I/O ever taken,
    // since it never matched), the game ran fine through a full session. The very next version -
    // identical except for ALSO logging the first ~30 calls unconditionally - crashed after
    // exactly one successful log line. That points to unsynchronized concurrent file I/O: if
    // magma::Id::Hash is called from multiple threads (plausible during parallel class
    // registration/asset init), two threads calling fopen/fprintf/fclose on the same file with zero
    // locking is a textbook crash, unrelated to hooking mechanics at all. This guards every bit of
    // file I/O in this plugin (both hooks) with one critical section.
    CRITICAL_SECTION g_logLock;

    using CRC32HashFn = unsigned int(__cdecl*)(const char*);
    CRC32HashFn g_originalCrc32Hash = nullptr;

    unsigned int __cdecl CRC32Hash_Detour(const char* str) {
        unsigned int hash = g_originalCrc32Hash(str);
        if (g_crc32LogFile != nullptr && str != nullptr) {
            EnterCriticalSection(&g_logLock);
            std::fprintf(g_crc32LogFile, "%08X\t%s\n", hash, str);
            std::fflush(g_crc32LogFile);
            LeaveCriticalSection(&g_logLock);
        }
        return hash;
    }

    using IdHashFn = void(__cdecl*)(unsigned int*, const char*);
    IdHashFn g_originalIdHash = nullptr;

    constexpr unsigned int kTargetHash = 0x86F001E3;
    volatile long g_reported = 0; // guards against reporting the target match more than once

    void __cdecl IdHash_Detour(unsigned int* outHash, const char* str) {
        g_originalIdHash(outHash, str);

        if (outHash != nullptr && *outHash == kTargetHash) {
            if (InterlockedCompareExchange(&g_reported, 1, 0) == 0) {
                // Only ever runs once - safe to do things that would be too risky on every call.
                char line[256];
                std::snprintf(line, sizeof(line), "MATCH %08X\t%s", *outHash,
                              str != nullptr ? str : "(null)");
                EnterCriticalSection(&g_logLock);
                FILE* f = std::fopen("id_hash_found.txt", "a");
                if (f != nullptr) {
                    std::fprintf(f, "%s\n", line);
                    std::fclose(f);
                }
                LeaveCriticalSection(&g_logLock);
                if (g_api != nullptr) {
                    g_api->Log(line);
                }
                MessageBoxA(nullptr, str != nullptr ? str : "(null)", "Found 0x86F001E3!", MB_OK);
            }
        }
    }

    // --- Register hook: catches classes that never call Id::Hash at runtime -------------

    using RegisterFn = void(__cdecl*)(void*);
    RegisterFn g_originalRegister = nullptr;

    // Self-contained CRC-32/IEEE-802.3 (poly 0xEDB88320 reflected, init 0xFFFFFFFF, final
    // complement) - confirmed byte-for-byte identical to magma::Id::Hash and Python's zlib.crc32
    // (see docs/docs/file-formats/mgb.md). Deliberately does NOT call the game's own Id::Hash -
    // keeps this hook's hot path free of any call into game code beyond the original Register.
    unsigned int Crc32(const char* str) {
        static unsigned int table[256];
        static bool ready = false;
        if (!ready) {
            for (unsigned int i = 0; i < 256; ++i) {
                unsigned int c = i;
                for (int k = 0; k < 8; ++k) {
                    c = (c & 1) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
                }
                table[i] = c;
            }
            ready = true;
        }
        unsigned int crc = 0xFFFFFFFFu;
        for (const unsigned char* p = reinterpret_cast<const unsigned char*>(str); *p != 0; ++p) {
            crc = table[(crc ^ *p) & 0xFF] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    // vtable slot +4 (index 1) on the ObjectTypeInfo itself - thiscall, zero args, returns a
    // pointer whose first field (offset 0) is the class's const char* name. Confirmed via
    // disassembly of magma::objecttypemanager::Initialize (0x10a98ad0 on FarCry2_server): it calls
    // this exact slot 3 times (a lazy/cached getter) before dereferencing the result once and
    // passing that to Id::Hash.
    using GetNameDescriptorFn = void*(__thiscall*)(void*);

    // Wrapped in SEH since typeInfo's real layout/vtable-slot-count is only confirmed for the
    // handful of classes this session's static analysis already read - an unexpected class shape
    // should fail closed (skip this registration) rather than crash the whole game.
    bool SafeGetName(void* typeInfo, const char** outName) {
        __try {
            if (typeInfo == nullptr) return false;
            void** vtable = *reinterpret_cast<void***>(typeInfo);
            if (vtable == nullptr) return false;
            auto getDesc = reinterpret_cast<GetNameDescriptorFn>(vtable[1]);
            if (getDesc == nullptr) return false;
            void* descriptor = getDesc(typeInfo);
            if (descriptor == nullptr) return false;
            const char* name = *reinterpret_cast<const char**>(descriptor);
            if (name == nullptr) return false;
            *outName = name;
            return true;
        } __except (EXCEPTION_EXECUTE_HANDLER) {
            return false;
        }
    }

    volatile long g_registerMatchReported = 0; // guards against reporting more than once

    void __cdecl Register_Detour(void* typeInfo) {
        g_originalRegister(typeInfo);

        const char* name = nullptr;
        if (!SafeGetName(typeInfo, &name)) {
            return;
        }
        if (Crc32(name) != kTargetHash) {
            return;
        }
        if (InterlockedCompareExchange(&g_registerMatchReported, 1, 0) == 0) {
            // Only ever runs once - safe to do things that would be too risky on every call.
            char line[256];
            std::snprintf(line, sizeof(line), "REGISTER MATCH %08X\t%s", kTargetHash, name);
            EnterCriticalSection(&g_logLock);
            FILE* f = std::fopen("register_hash_found.txt", "a");
            if (f != nullptr) {
                std::fprintf(f, "%s\n", line);
                std::fclose(f);
            }
            LeaveCriticalSection(&g_logLock);
            if (g_api != nullptr) {
                g_api->Log(line);
            }
            MessageBoxA(nullptr, name, "Found 0x86F001E3 via Register!", MB_OK);
        }
    }
}

extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api) {
    g_api = api;

    if (api->apiVersion != FCSE_API_VERSION) {
        return false;
    }

    InitializeCriticalSection(&g_logLock);

    constexpr uintptr_t kDuniaPreferredBase = 0x10000000;
    constexpr uintptr_t kCrc32HashRva = 0x10229400;
    constexpr uintptr_t kIdHashRva = 0x10aa7150;

    g_crc32LogFile = std::fopen("hash_log.txt", "w");
    if (g_crc32LogFile != nullptr) {
        void* target = reinterpret_cast<void*>(api->duniaBase + (kCrc32HashRva - kDuniaPreferredBase));
        if (api->Hook(target, reinterpret_cast<void*>(&CRC32Hash_Detour),
                      reinterpret_cast<void**>(&g_originalCrc32Hash))) {
            api->Log("hash_logger_plugin: CRC32_Hash hook installed, logging to bin\\hash_log.txt");
        } else {
            api->Log("hash_logger_plugin: failed to hook CRC32_Hash");
        }
    } else {
        api->Log("hash_logger_plugin: could not open hash_log.txt");
    }

    void* idHashTarget = reinterpret_cast<void*>(api->duniaBase + (kIdHashRva - kDuniaPreferredBase));
    if (api->Hook(idHashTarget, reinterpret_cast<void*>(&IdHash_Detour),
                  reinterpret_cast<void**>(&g_originalIdHash))) {
        api->Log("hash_logger_plugin: magma::Id::Hash hook installed (fires only on "
                 "0x86F001E3 match)");
    } else {
        api->Log("hash_logger_plugin: failed to hook magma::Id::Hash");
    }

    constexpr uintptr_t kRegisterRva = 0x10a982b0;
    void* registerTarget = reinterpret_cast<void*>(api->duniaBase + (kRegisterRva - kDuniaPreferredBase));
    if (api->Hook(registerTarget, reinterpret_cast<void*>(&Register_Detour),
                  reinterpret_cast<void**>(&g_originalRegister))) {
        api->Log("hash_logger_plugin: magma::objecttypemanager::Register hook installed (fires "
                 "only on 0x86F001E3 match, computed locally without calling Id::Hash)");
    } else {
        api->Log("hash_logger_plugin: failed to hook magma::objecttypemanager::Register");
    }

    return true;
}
