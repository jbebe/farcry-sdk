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

#define FCSE_API_VERSION 5

// Which Dunia.dll the game is running. Far Cry 2 v1.03 shipped as two different PC builds whose
// images place the same code at different addresses, so a raw RVA is only ever true of one of them.
// A plugin that resolves everything through ResolveFrom() below never has to look at this; it is
// here for plugins that genuinely need to branch on the build.
typedef enum FCSE_GameBuild {
    FCSE_GAME_BUILD_UNKNOWN = 0,
    FCSE_GAME_BUILD_103_RETAIL = 1,  // GOG (Fortune's Edition) / patched retail
    FCSE_GAME_BUILD_103_UPLAY = 2,   // Steam / Ubisoft Connect re-release
} FCSE_GameBuild;

// Resolves an address you confirmed in ONE SPECIFIC BUILD to the equivalent address in whichever
// build is running. Returns 0 if that RVA is not one the address library knows.
//
// This is the entry point most plugins should use, and the one that matches how addresses are
// actually obtained. You open a Dunia.dll in Ghidra, IDA or Cheat Engine, you find a function, and
// what you have is an address *in the build you opened* - so say which build that was:
//
//     api->ResolveFrom(FCSE_GAME_BUILD_103_UPLAY,  0x0081E9C0)   // found in Steam's DLL
//     api->ResolveFrom(FCSE_GAME_BUILD_103_RETAIL, 0x00811C00)   // found in GOG's DLL
//
// Both name the same function, and both work whichever build the player is on. You need only the
// copy of the game you already have.
//
// The two builds are NOT a fixed distance apart - the offset between them takes 2,608 different
// values across the mapping - so this lookup is the only correct way to translate. Pass the RVA
// (0x0081E9C0) or the VA at Dunia's preferred base (0x1081E9C0); both are accepted, since no real
// RVA reaches 0x10000000.
typedef uintptr_t (*FCSE_ResolveFromFn)(FCSE_GameBuild sourceBuild, uint32_t rva);

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
// the plugin - and one row on FCSE's own Mod Configuration Menu page, reached from the Options
// screen (see the FCSE README's "Mod Configuration Menu" section for the mechanism). A plugin that
// registers nothing gets no group in the file: there is nothing to configure, so nothing is written.
//
// FCSE owns the stored value - a plugin never holds a pointer to it. Changes arrive through the
// callback below, twice over: once during registration (before any Dunia.dll engine code runs)
// carrying whatever the config file holds, and again after every in-game change.
//
// Each type maps onto a control the game's own settings pages already use, so a mod's page looks
// like a stock one. The row a type produces, and where its extra configuration comes from, is
// documented on FCSE_Setting below.
typedef enum FCSE_SettingType {
    FCSE_SettingType_Checkbox = 0, // a bool; a YES/NO spinner. Serialized as `true`/`false`
    FCSE_SettingType_Choice = 1,   // one of `choices`; a < value > spinner. Serialized as the label
    FCSE_SettingType_Slider = 2,   // an int in [minValue, maxValue]; a slider. Serialized as itself
    FCSE_SettingType_Text = 3,     // a string; a row opening the game's text prompt. Serialized raw
} FCSE_SettingType;

// A setting's value, tagged with its own type so this one callback signature keeps working as the
// enum above grows. Read the member matching `type`; reading any other member is undefined.
//
// `asNumber` is deliberately first and deliberately overlaps the three numeric members: a braced
// initializer writes the first member only (designated initializers are C99/C++20 and this header
// has to work in neither), so it is what the FCSE_* macros below set, and the named member is what
// you read. That aliasing assumes a little-endian 32-bit target, which Far Cry 2 always is.
typedef struct FCSE_SettingValue {
    FCSE_SettingType type;
    union {
        int32_t asNumber;   // what the initializer macros write; rarely what you want to read
        bool asCheckbox;    // Checkbox
        uint32_t asChoice;  // Choice - an index into FCSE_Setting::choices
        int32_t asSlider;   // Slider
        const char* asText; // Text - NUL-terminated UTF-8, FCSE-owned, valid for the call only
    };
} FCSE_SettingValue;

// Convenience initializers for a default value, e.g.
//   { "Verbose logging", FCSE_CHECKBOX(false), &OnVerboseChanged, NULL }
//
// A Text setting's default is FCSE_Setting::defaultText rather than part of the value, because a
// pointer cannot be written through the integer member a braced initializer reaches.
#define FCSE_CHECKBOX(defaultValue)                                                                \
    {                                                                                              \
        FCSE_SettingType_Checkbox, { (defaultValue) ? 1 : 0 }                                      \
    }
#define FCSE_CHOICE(defaultIndex)                                                                  \
    {                                                                                              \
        FCSE_SettingType_Choice, { (int32_t)(defaultIndex) }                                       \
    }
#define FCSE_SLIDER(defaultValue)                                                                  \
    {                                                                                              \
        FCSE_SettingType_Slider, { (int32_t)(defaultValue) }                                       \
    }
#define FCSE_TEXT()                                                                                \
    {                                                                                              \
        FCSE_SettingType_Text, { 0 }                                                               \
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
// Everything past `userdata` is per-type configuration rather than a value: it is fixed for the
// life of the setting, where the value changes every time the player touches the row. Fields that
// do not apply to this setting's type are ignored, and C's trailing zero-initialization means a
// Checkbox declaration never has to mention any of them:
//
//   { "Verbose logging", FCSE_CHECKBOX(false), &OnVerboseChanged, NULL }
//   { "Difficulty", FCSE_CHOICE(1), &OnDifficulty, NULL, kLabels, 3 }
//   { "Draw distance", FCSE_SLIDER(6), &OnDrawDistance, NULL, NULL, 0, 1, 10 }
//   { "Server name", FCSE_TEXT(), &OnServerName, NULL, NULL, 0, 0, 0, "kilimanjaro", 24 }
typedef struct FCSE_Setting {
    const char* name;
    FCSE_SettingValue defaultValue;
    FCSE_SettingChangedFn onChanged; // optional; NULL means "store it, just don't tell me"
    void* userdata;                  // opaque, passed back to onChanged unmodified

    // Choice: the option labels, in the order the player cycles through them, and how many there
    // are. Both are copied during registration, so neither has to outlive the call. A Choice with
    // fewer than two labels is rejected - it would be a row the player cannot change.
    const char* const* choices;
    uint32_t choiceCount;

    // Slider: the inclusive bounds. minValue must be < maxValue; the stored value is clamped into
    // range on load, so narrowing the range in a later version of a plugin cannot produce an
    // out-of-range value.
    int32_t minValue;
    int32_t maxValue;

    // Text: the initial string, and the longest the player may type. NULL defaultText means empty.
    // maxTextLength of 0 means FCSE's own cap applies.
    const char* defaultText;
    uint32_t maxTextLength;
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
    // Dunia.dll's load base and on-disk size. Still here, and still correct, but adding your own
    // hardcoded RVA to duniaBase is what makes a plugin work on one build and crash on the other -
    // and duniaSize is no longer the right version gate, because two files of different sizes can
    // be the same image. Prefer ResolveFrom(), and read gameBuild if you must branch.
    uintptr_t duniaBase;
    size_t duniaSize;

    FCSE_LogFn Log;

    // --- address library (API v5) --------------------------------------------------------------
    // What makes a plugin build-agnostic: hand it an address as you found it in one build, get the
    // equivalent in the build that is running. There is deliberately only one way to do this - an
    // address in a build you have open is the only handle a plugin author actually possesses.
    //
    // fcse_relocation.h wraps it as typed, lazily-resolved function pointers.
    FCSE_ResolveFromFn ResolveFrom;

    FCSE_GameBuild gameBuild;      // which build this is
    const char* gameBuildId;       // e.g. "fc2_103_uplay" - stable, loggable
    const char* addressMapping;    // address-mapping version, e.g. "1.0"

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
// this plugin (e.g. an apiVersion or gameBuild you don't support) - FCSE logs the refusal and
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
//
// Build requirements, in full:
//
//   - 32-bit (x86). Far Cry 2 is a 32-bit process; a 64-bit DLL cannot load into it.
//   - Any compiler, any version. This whole header is deliberately pure C - POD structs, function
//     pointers, const char* - with no std:: types, no virtuals and no exceptions crossing the
//     boundary, so a plugin never has to match the toolchain FCSE.exe was built with. (Nor the one
//     the game was built with: FCSE reaches engine internals through hand-declared __thiscall
//     signatures and byte offsets, so Dunia's own layout rules apply regardless.)
//   - /MT (static CRT) is recommended, not required. FCSE.exe uses it so players need no Visual
//     C++ redistributable installed; a /MD plugin works fine but reintroduces that requirement for
//     anyone who installs it. Either way each module carries its own CRT, so the usual rule holds:
//     never free in one module what another allocated. Nothing in this API transfers ownership -
//     strings passed in are copied, and callbacks hand back values, not buffers - so there is no
//     way to trip over that through the API itself.
