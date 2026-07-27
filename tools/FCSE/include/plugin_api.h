// Public ABI for FCSE (Far Cry Script Extender) plugins.
//
// A plugin is a plain DLL, dropped into bin\plugins\, that exports at least FCSE_Load. FCSE.exe
// loads every plugin DLL right after resolving Dunia.dll, before any Dunia.dll engine code beyond
// its own DllMain/CRT init has run - see the FCSE README for the full lifecycle.
//
// This header has no dependency on the rest of the FCSE source tree - copy it into a plugin
// project as-is.
#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#include <windows.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define FCSE_API_VERSION 1

// Matches Dunia.dll's own AddFunctionCB(void* fn, const char* name) signature exactly - the
// function pointer stored is called later, by engine code, with whatever argument count/types
// that specific named callback expects (see docs/docs/engine-internals/function-registry.md).
typedef void (*FCSE_AddFunctionCBFn)(void* fn, const char* name);

// Detours `target` to `detour` (MinHook-backed). On success, `*original` receives a callable
// trampoline that runs the original function's overwritten prologue before jumping back into the
// rest of the original function - call through it to preserve original behavior around your hook.
// Returns false (and logs why) if `target` is null, MinHook itself fails, or another plugin
// already owns a hook on this exact address - FCSE does not chain multiple hooks on one address,
// first claimant wins.
typedef bool (*FCSE_HookFn)(void* target, void* detour, void** original);

// Overwrites `size` bytes at `address` with `data` (handles the VirtualProtect dance so `address`
// doesn't need to already be writable). Returns false (and logs why) if the byte range overlaps a
// range a *different* plugin already patched this run - overlapping your own earlier patch is
// fine. Meant for the same kind of small constant/branch-flip edit as reverse/patch_*.py apply
// statically to Dunia.dll on disk, just live and in-process instead.
typedef bool (*FCSE_PatchFn)(void* address, const void* data, size_t size);

// Writes one line to bin\fcse.log, tagged with the calling plugin's own module name (resolved
// automatically - no need to pass an identifier). See the FCSE README for the exact line format.
typedef void (*FCSE_LogFn)(const char* message);

typedef struct FCSE_PluginAPI {
    uint32_t apiVersion; // Always FCSE_API_VERSION for this struct layout - compare before using
                         // any field below, in case a future loader version adds/reorders fields.

#ifdef _WIN32
    HMODULE duniaModule;
#else
    void* duniaModule;
#endif
    uintptr_t duniaBase; // Dunia.dll's load base - add your own confirmed RVA to get a live VA.
    size_t duniaSize;    // On-disk size of the loaded Dunia.dll, for your own version gate (only
                         // trust hardcoded RVAs against the exact build you confirmed them on -
                         // see docs/docs/engine-internals/overview.md for known build sizes).

    FCSE_LogFn Log;

    // Tier 1: valid to call ONLY from FCSE_OnRegisterFunctions (the only point at which Dunia's
    // function registry is guaranteed constructed). Calling it from FCSE_Load is undefined.
    FCSE_AddFunctionCBFn AddFunctionCB;

    // Tier 2/3: valid to call from FCSE_Load (or later).
    FCSE_HookFn Hook;
    FCSE_PatchFn Patch;
} FCSE_PluginAPI;

// Required export. Called once per plugin, right after FCSE.exe loads Dunia.dll and before any
// Dunia.dll engine code runs. Install Hook()/Patch() calls here. Return false to abort loading
// this plugin (e.g. an apiVersion or duniaSize you don't support) - FCSE logs the refusal and
// continues with the remaining plugins.
typedef bool (*FCSE_LoadFn)(const FCSE_PluginAPI* api);

// Optional export. Called later, exactly when Dunia.dll invokes the function-registry provider
// callback (after InitDuniaEngine succeeds) - the only safe time to call AddFunctionCB. Plugins
// run in load order, and all run BEFORE FCSE's own stock RegisterDebugCommands registrations, so a
// plugin can override one of the 12 stock names (see the FCSE README) - Dunia's own
// FunctionRegistry_Insert is first-claimant-wins and silently ignores later registrations of an
// already-claimed name, so FCSE's own AddFunctionCB wrapper rejects (and logs) later claimants
// itself rather than relying on that silent behavior.
typedef void (*FCSE_OnRegisterFunctionsFn)(const FCSE_PluginAPI* api);

#ifdef __cplusplus
}
#endif

// Plugin DLLs export these two by name (not through this header - a plugin .cpp defines them with
// extern "C" and the exact names below):
//   extern "C" __declspec(dllexport) bool FCSE_Load(const FCSE_PluginAPI* api);
//   extern "C" __declspec(dllexport) void FCSE_OnRegisterFunctions(const FCSE_PluginAPI* api); // optional
