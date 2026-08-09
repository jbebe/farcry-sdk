# FCSE (Far Cry Script Extender)

An SKSE-style DLL plugin loader for Far Cry 2 - see [`docs/docs/todos.md`](../../docs/docs/todos.md)'s
`Tools/"dll plugins"` entry for the motivating problem: some engine behavior only lives in
`Dunia.dll` itself, and until now the only way to change it was to ship your own patched copy of
that DLL - which works for exactly one mod at a time, since two patched copies can't coexist.

This README is for people building/maintaining the loader itself. If you just want to write a
plugin, see [`include/plugin_api.h`](include/plugin_api.h) (the full ABI, documented inline) and
[`example_plugin/example_plugin.cpp`](example_plugin/example_plugin.cpp) (a working, minimal one).
If you just want to install plugins into the game, see [`plugins/README.md`](plugins/README.md).

## How it works

Ships as a separate exe, `FCSE.exe`, dropped into the game's `bin\` folder next to the untouched
`FarCry2.exe`. Reimplements `FarCry2.exe`'s own `WinMain` - which is, in its entirety,

```c
RegisterGameFunctionProvider(&RegisterDebugCommands);
RunGame(hInstance, cmdLine);
```

(see [`docs/docs/engine-internals/launcher-exe.md`](../../docs/docs/engine-internals/launcher-exe.md))
- both calls resolve to plain **exports of `Dunia.dll`**, not anything statically bound into the
exe. `AddFunctionCB` (also a `Dunia.dll` export) is the third piece: the function-registry insert
that `RegisterDebugCommands` calls 15 times. All three are resolved by name via `GetProcAddress`
(`src/dunia_api.cpp`) - confirmed live against the Steam v1.03 build's export table
(`RegisterGameFunctionProvider`/`AddFunctionCB` are plain undecorated C exports; `RunGame` needs
its mangled name, `?RunGame@@YA_NPAUHINSTANCE__@@PBD@Z`) - so, unlike
[`tools/misc/modpatcher`](../misc/modpatcher), this loader has **no hardcoded-RVA dependency on a
specific `Dunia.dll` build**. Only plugins that choose to hook/patch internals need their own
version gate (`FCSE_PluginAPI::duniaSize` is provided for exactly that).

### Startup sequence (`src/main.cpp`)

1. Resolve `Dunia.dll` next to the loader, `GetProcAddress` the 3 exports above
   (`src/dunia_api.cpp`).
2. Read the real `MalariaCurve`/`PlayerSPFinalize` constants straight out of the real
   `FarCry2.exe` (`src/stock_constants.cpp`) - see "Reimplementing the 12 stock handlers" below for
   why this is read at runtime instead of hardcoded.
3. `MH_Initialize()` (MinHook, vendored via `CMakeLists.txt`'s `FetchContent`, same pattern as
   `tools/misc/modpatcher`).
4. Read `bin\fcse.ini` into memory (`src/settings_registry.cpp`). Must happen before any plugin
   loads: registration resolves each setting against this file and calls the plugin back with the
   result, so the file has to be there first. A missing file is the normal first-run case.
5. Build the `FCSE_PluginAPI` struct and load every `*.dll` in `bin\plugins\`
   (`src/plugin_loader.cpp`), calling each one's required `FCSE_Load` export. This is the earliest
   safe point for a plugin to install `Hook()`/`Patch()` calls - nothing in `Dunia.dll` beyond its
   own `DllMain`/CRT init has run yet, and it's where plugins declare their settings.
6. Write `bin\fcse.ini` back if anything changed. Every plugin has now declared what it has, so
   one write completes the file - a first run leaves a fully hand-editable config without the
   player ever opening the in-game menu.
7. `RegisterGameFunctionProvider(&DebugCommands::Provider)` - `Provider()` is the callback
   `Dunia.dll` invokes later, from inside `RunGame`, once `InitDuniaEngine` has succeeded (the only
   point at which `Dunia.dll`'s function registry is guaranteed constructed). It runs, **in this
   order**:
   a. every loaded plugin's optional `FCSE_OnRegisterFunctions` export, then
   b. this loader's own reimplementation of the 12 stock handlers.

   The order matters: `FunctionRegistry_Insert` (confirmed via live decompile, `0x10299430`) is a
   find-first insert - the *first* registrant for a name wins, a second registration of an
   already-claimed name is a **silent no-op** inside `Dunia.dll` itself. Running plugins first is
   what lets a plugin override one of the 12 stock names (e.g. change `AddDiamond`'s effect) -
   registering stock handlers first would make that impossible.
8. `RunGame(hInstance, cmdLine)` - the game proceeds normally from here.

### Reimplementing the 12 stock handlers (`src/debug_commands.cpp`)

`RegisterDebugCommands` isn't a config table, it's a callback registry bootstrap - and
[`docs/docs/engine-internals/function-registry.md`](../../docs/docs/engine-internals/function-registry.md)
confirms several of its 12 handlers are live gameplay hooks (diamond pickups, malaria progression,
main-menu construction, loading-screen text), not just QA stubs. `FCSE.exe` reproduces all 12
byte-for-byte so nothing regresses versus the stock exe. Two of them (`MalariaCurve`,
`PlayerSPFinalize`) depend on float/int constants baked into `FarCry2.exe`'s own data section
that were never RE'd to an exact value - rather than hardcode a guess, `src/stock_constants.cpp`
maps the real `FarCry2.exe` (via `LoadLibraryExW(..., DONT_RESOLVE_DLL_REFERENCES)`) and reads
them directly by VA at startup, so the reimplementation is exactly as faithful as whatever build
is actually installed.

## The plugin API - four tiers

See `include/plugin_api.h` for the authoritative, documented ABI. Summary, from "no RE required" to
"full control":

1. **`AddFunctionCB(fn, name)`** - claim one of `Dunia.dll`'s named callback slots. Zero address
   knowledge needed, and version-independent (it's a string key). `function-registry.md` already
   documents ~17 real gameplay call sites reachable this way.
2. **`Hook(target, detour, &original)`** - MinHook-backed function detouring, for internals with no
   existing named-callback seam. `target` is `duniaBase + <an RVA you found via your own Ghidra
   work>` (or any other module's export, like `example_plugin`'s kernel32 demo).
3. **`Patch(address, data, size)`** - direct byte patching (`VirtualProtect` → `memcpy` → restore →
   `FlushInstructionCache`), for the same kind of small constant/branch-flip edit
   `reverse/patch_toRed.py`/`patch_incHB.py`/`patch_carJoke.py` apply *statically* to `Dunia.dll` on
   disk today - this applies the same kind of edit live, in-process, so any number of plugins can
   each patch their own byte ranges without needing one shared pre-patched file.
4. **`RegisterSettings(pluginName, settings, count)`** - persistent, player-editable settings, both
   in `bin\fcse.ini` and as rows in the in-game Mod Configuration Menu. Zero address knowledge
   needed. See below.

### Settings and `bin\fcse.ini`

A plugin declares what it has - a name, a type, a default, and a callback - and FCSE owns the stored
value from there. Registration is valid from `FCSE_Load`:

```c
static const char* const kVerbosity[] = {"Quiet", "Normal", "Verbose"};

static const FCSE_Setting settings[] = {
    {"Toggle toRed",  FCSE_CHECKBOX(false), &OnToRedChanged, NULL},
    {"Log verbosity", FCSE_CHOICE(1),       &OnChanged, NULL, kVerbosity, 3},
    {"Demo slider",   FCSE_SLIDER(5),       &OnChanged, NULL, NULL, 0, 0, 10},
    {"Demo text",     FCSE_TEXT(),          &OnChanged, NULL, NULL, 0, 0, 0, "kilimanjaro", 24},
};
api->RegisterSettings("example_plugin", settings, 4);
```

Which produces, and thereafter reads back from, a group named after the plugin:

```ini
[example_plugin]
Toggle toRed = false
Log verbosity = Normal
Demo slider = 5
Demo text = kilimanjaro
```

Four types, each rendering as the control the game's own settings pages use, so a mod's page looks
like a stock one:

| Type | Control | Serialized as |
|---|---|---|
| `Checkbox` | a YES/NO spinner | `true` / `false` |
| `Choice` | a `< value >` spinner over `choices` | the chosen **label**, so the file stays readable; an index is accepted on read |
| `Slider` | a draggable slider over `[minValue, maxValue]` | the integer |
| `Text` | a row showing the value; activating it opens the game's own modal text prompt | the raw string |

Everything after `userdata` in `FCSE_Setting` is per-type configuration, ignored by the types that
do not use it - which is why a `Checkbox` never has to mention any of it. A `Choice` needs at least
two labels and a `Slider` needs `minValue < maxValue`, or the setting is rejected and logged; a
default that is merely out of range is clamped rather than costing the plugin its row.

Three properties worth knowing:

- **The callback is the only channel.** It fires once from inside `RegisterSettings` - synchronously,
  before that call returns, and therefore before any `Dunia.dll` engine code runs - carrying
  whatever the file held (or the declared default if it held nothing usable). It fires again after
  every in-game toggle. A plugin never needs a separate "read my config" step, and never holds a
  pointer into FCSE's storage.
- **A plugin that registers nothing gets no group** in the file - there is nothing to toggle, so
  nothing is written. It still appears in the in-game menu, marked `(no settings)`: that page lists
  what actually loaded, so it answers "which mods do I have?" as well as "what can I change?".
- **Groups for plugins you no longer have installed are kept, not deleted.** The file is the union
  of every plugin that has ever run, but a given launch only sees what's installed now; `src/ini_file.cpp`
  is order- and comment-preserving specifically so uninstalling a plugin for one session doesn't
  discard its settings. `tests/ini_file_tests.cpp` covers that, plus the load/save fixed point (the
  whole file is rewritten on every toggle, so anything the writer synthesises and the reader keeps
  would otherwise accumulate without bound).

This replaced a `bool*`-based API in `FCSE_API_VERSION` 3. The inversion is what made persistence
possible at all: the old version only knew *where* a plugin's bool lived, never what it meant or
what to call it in a file, so it could never write one back. `FCSE_API_VERSION` 4 added the three
types beyond `Checkbox`, which grew `FCSE_Setting` - so a plugin built against 3 must be rebuilt.

### Conflict handling

Two plugins can legitimately target the same name/address. Rather than build a composable
hook-chaining dispatcher, **FCSE tracks per-resource ownership and rejects the second claimant**,
logging both plugin names - loud and debuggable instead of silently misbehaving:

- `AddFunctionCB`: `src/function_registry.cpp` tracks name → owning module, independent of (and in
  addition to) `Dunia.dll`'s own silent no-op.
- `Hook`: `src/hook.cpp` tracks target address → owning module, on top of MinHook's own
  `MH_ERROR_ALREADY_CREATED` rejection.
- `Patch`: `src/patch.cpp` tracks claimed `(address, size)` ranges; a new claim overlapping a
  *different* module's existing claim is rejected. Overlap with your own earlier claim is fine.

In every case, "which module is calling" is resolved automatically via
`GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, _ReturnAddress(), ...)`
(`src/caller_identity.cpp`) - no plugin ever passes its own identity into an API call, so it can't
get the tag wrong, and this loader's own stock registrations (tagged `FCSE`, since the call site is
inside `FCSE.exe`'s own module) go through the exact same conflict-tracking as any plugin.

### Logging

Every run (over)writes `bin\fcse.log`. Every line - whether from the loader itself or from a
plugin via `FCSE_PluginAPI::Log` - goes through the one writer in `src/log.cpp`, so the format can
never drift between the two sources:

```
[2026-07-27 06:04:03.12341234][fcse] Dunia.dll resolved, base=0x10000000 size=20183176 bytes
[2026-07-27 06:04:03.12341235][example_plugin] example_plugin loaded
```

Timestamps are local time at Windows' native 100ns `FILETIME` resolution (via
`GetSystemTimePreciseAsFileTime`), formatted as 8 fractional digits (7 real + 1 padding zero) - fine
enough to disambiguate lines from the loader and multiple plugins landing within the same
millisecond, which plain millisecond timestamps aren't.

## Building

Requires the `x86-debug` or `x86-release` CMake preset - **never `x64-*`**: Far Cry 2 is a 32-bit
process, and neither `FCSE.exe` nor a plugin DLL built for it can load as 64-bit.

```
.\build.ps1            # release (default), build only
.\build.ps1 -Config debug
.\build.ps1 -Tests     # also run ctest
.\build.ps1 -Zip       # also package out\fcse-release.zip
```

Same `vswhere`/`vcvarsall.bat x86` dance as `tools/misc/modpatcher/build.ps1`. Builds `FCSE.exe`
plus `example_plugin.dll`. Tests are opt-in via `-Tests`; run them through `build.ps1` rather than
calling `ctest` directly - like `cmake`, it only resolves from the developer environment this
script sets up.

`-Zip` packages `out\fcse-{Config}.zip` in the install layout below, so its contents extract
straight into the game's `bin\`:

```
FCSE.exe
plugins\example_plugin.dll
```

FCSE's settings-page layout isn't in that list because it's *inside* `FCSE.exe`: both `.mgb`
variants are embedded as `RCDATA` resources at build time (`assets/fcse.rc.in`, wired up in
`CMakeLists.txt`), so installing the loader is copying one file and there's no second file to
forget, mismatch, or lose.

This is a local convenience package - it includes `example_plugin.dll`, which the GitHub release
workflow (`.github/workflows/fcse-release.yml`) deliberately ships as a *separate* download.

## Installing

1. Build produces `FCSE.exe` (see the build output directory for the preset you used).
2. Copy it into the game's `bin\` folder, next to the existing `FarCry2.exe` - leave that file
   alone, `FCSE.exe` is a separate, additional way to launch the game, not a replacement.
3. Drop plugin DLLs into `bin\plugins\` (created automatically on first run if missing).
4. Launch `FCSE.exe` instead of `FarCry2.exe`. Check `bin\fcse.log` to confirm `Dunia.dll` resolved,
   which plugins loaded, and whether anything was rejected as a conflict.

## Verification

- `cmake --preset x86-release` + build succeeds, produces a genuinely 32-bit `FCSE.exe` with
  `/LARGEADDRESSAWARE` set - confirm via `dumpbin /headers` showing
  `Application can handle large (>2GB) addresses`.
- `dumpbin /dependents` on `FCSE.exe` lists **only `KERNEL32.dll` and `USER32.dll`**. Anything
  starting `MSVCP`/`VCRUNTIME`/`api-ms-win-crt-` means the static-CRT setting silently stopped
  applying, and players without the matching Visual C++ redistributable would get a missing-DLL box
  before a single line of FCSE runs - no `fcse.log`, nothing to diagnose from. See the `CMP0091`
  note in `CMakeLists.txt` for the failure mode that produces exactly that.
- Both `.mgb` variants are really embedded: `FindResourceW(exe, L"FCSE_MGB", RT_RCDATA)` and
  `FCSE_MGB_WIDESCREEN` return blobs byte-identical to `assets/*.mgb`. A `.rc` added to a project
  without `enable_language(RC)` is skipped silently, so a build with no settings page in it looks
  perfectly healthy until the game runs.
- Without the real game present: point `FCSE.exe` at a folder with no `Dunia.dll` and confirm it
  logs a clear failure (`fcse.log`) and shows a message box instead of crashing.
- `.\build.ps1 -Tests` runs `tests/ini_file_tests.cpp` (the config file's reader/writer) via
  `ctest`. It needs neither the game nor `Dunia.dll`.
- Drop `example_plugin.dll` alone into `bin\plugins\`: `fcse.log` should show it discovered, loaded,
  its `GetTickCount` hook installed, its demo buffer patched, and (later, from inside `Provider()`)
  its `toRed` registration accepted.
- Settings round trip: with `example_plugin.dll` installed, a first launch should create
  `bin\fcse.ini` containing an `[example_plugin]` group. Toggle "Toggle toRed" in the Mod
  Configuration Menu, confirm the row's `[ON]`/`[OFF]` flips on the spot and the file updates, then
  relaunch and confirm `fcse.log` shows `example_plugin: toRed is ON` during load - i.e. the value
  came back from the file before the game started, not from the plugin's own default.
- Conflict rejection: install a second copy of `example_plugin.dll` under a different filename.
  Both are loaded (`bin\plugins\` is scanned via `FindFirstFileW`/`FindNextFileW`, so order follows
  normal directory enumeration, not necessarily alphabetical), and `fcse.log` should show exactly
  one of them win the `GetTickCount` hook and the `toRed` registration, with the other's attempt
  logged as a rejected conflict naming both. `Patch()`'s overlap-rejection path is exercised by
  `example_plugin` alone (against its own local buffer) and reviewed by inspection
  (`src/patch.cpp`'s interval-overlap check is a few lines) - it doesn't have an equally safe,
  generic **cross**-plugin demo target the way a real Windows API export does for `Hook()`.
- **Real in-game verification is on you** - same as `tools/misc/modpatcher`'s own README status
  section, whose live-launch testing was done against a real Steam install, not by an agent. A
  real launch + gameplay pass (menu loads, a diamond pickup still increments, malaria curve still
  behaves, and - with `example_plugin` installed - the HUD actually renders red-channel-only) is
  the remaining step to confirm full behavioral parity with the stock exe plus the plugin
  mechanism actually reaching real gameplay code.
