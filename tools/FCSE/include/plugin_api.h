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

#define FCSE_API_VERSION 3

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

// Tier 4: persistent, player-editable settings.
//
// Every setting a plugin registers becomes one line in bin\fcse.ini - inside a group named after
// the plugin - and one row in the in-game Mod Configuration Menu (spliced into the Options menu's
// own tab list; see the FCSE README's "Mod Configuration Menu" section for the mechanism). A
// plugin that registers nothing gets no group in the file: there is nothing to toggle, so nothing
// is written.
//
// FCSE owns the stored value - a plugin never holds a pointer to it. Changes arrive through the
// callback below, twice over: once during registration (before any Dunia.dll engine code runs)
// carrying whatever the config file holds, and again after every in-game toggle.
typedef enum FCSE_SettingType {
    FCSE_SettingType_Checkbox = 0, // a bool; serialized as `true`/`false`
} FCSE_SettingType;

// A setting's value, tagged with its own type so this one callback signature keeps working as the
// enum above grows. Read the member matching `type`; reading any other member is undefined.
typedef struct FCSE_SettingValue {
    FCSE_SettingType type;
    union {
        bool asCheckbox;
    };
} FCSE_SettingValue;

// Convenience initializer for a Checkbox default, e.g.
//   { "Verbose logging", FCSE_CHECKBOX(false), &OnVerboseChanged, NULL }
#define FCSE_CHECKBOX(defaultValue)                                                                \
    {                                                                                              \
        FCSE_SettingType_Checkbox, { (defaultValue) }                                              \
    }

// Called with the setting's resolved value: once from inside RegisterSettings (synchronously,
// before that call returns), then again after each player toggle. `value` points at FCSE-owned
// storage that is only valid for the duration of the call - copy anything you need to keep.
typedef void (*FCSE_SettingChangedFn)(const FCSE_SettingValue* value, void* userdata);

// One registered setting. `name` is the key inside the plugin's own group, so it only has to be
// unique within that plugin - two plugins can each have a "Verbose logging". It must be non-empty
// and contain none of '=', '[', ']', CR or LF, since it becomes an INI key verbatim.
//
// `defaultValue.type` IS the setting's type - there is no separate type field that could drift out
// of sync with it. The default applies whenever the config file has no usable value for this
// setting (a fresh install, a newly added setting, or a value stored in an unparseable form), and
// is written back to the file so the player can see and edit it.
typedef struct FCSE_Setting {
    const char* name;
    FCSE_SettingValue defaultValue;
    FCSE_SettingChangedFn onChanged; // optional; NULL means "store it, just don't tell me"
    void* userdata;                  // opaque, passed back to onChanged unmodified
} FCSE_Setting;

// Registers `settingCount` settings under `pluginName`, in display order. Valid to call from
// FCSE_Load. `settings` is fully copied, so it does not need to outlive the call.
//
// `pluginName` scopes every setting in the call and names the group in bin\fcse.ini; it must be
// non-empty and free of '[', ']', CR and LF. Calling more than once with the same name appends to
// that plugin's existing group rather than starting a second one.
//
// Each setting's onChanged fires before this call returns - see FCSE_SettingChangedFn. Returns
// false (and logs why) if `pluginName`/`settings` is null, `settingCount` is 0, or `pluginName` is
// malformed. Individual settings that fail validation are skipped and logged, leaving the rest of
// the batch registered - so a true return means "at least one setting landed", not "all did".
typedef bool (*FCSE_RegisterSettingsFn)(const char* pluginName, const FCSE_Setting* settings,
                                         size_t settingCount);

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

    // Tier 4: valid to call from FCSE_Load. See FCSE_RegisterSettingsFn above.
    FCSE_RegisterSettingsFn RegisterSettings;
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
