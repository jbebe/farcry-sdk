# Changelog

Notable changes to FCSE, loosely following [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Fixed
- **Malaria and the player's damage handling stay stock when `FarCry2.exe` is missing.** FCSE read
  two of the game's values from `FarCry2.exe` and, without it, fell back to ones that made malaria
  attacks come about 60 times too often and zeroed a multiplier the player's damage handling uses.
  FCSE now carries the stock values itself.

## [1.4.0] - 2026-10-08

### Added
- **Mods can keep their own values on game entities** — a count on a weapon, a flag on a car — that
  travel with the entity through the savegame. A mod can also give an entity starting values in the
  entity library. The values live in a new engine component, `CFCSEDataComponent`; a game without
  FCSE skips it and still loads. C++ plugins get it as `FCSE_PluginAPI::EntityData`.
- The example plugin keeps a draw count on every weapon the player carries, to show how.
- **The engine's name hash for C++ plugins** — `FCSE::Crc32` in `fcse_api.h` hashes a name the way
  the engine does, at compile time if you like, or any run of bytes. Header-only, C++14 and later.

### Changed
- **Plugin DLLs built for an earlier FCSE no longer load** until their authors rebuild them, so
  update your plugins along with FCSE. `fcse.log` names each one that did not load. Lua scripts are
  not affected. For authors: the plugin ABI is now `FCSE_API_VERSION` 8, since `FCSE_PluginAPI`
  gained `EntityData`; a rebuild is all a plugin needs.
- For authors: `fcse_api.h` no longer includes `<windows.h>`, so its `min` and `max` macros no
  longer reach every plugin. A file that used Windows names through it includes `<windows.h>`
  itself, and `duniaModule` is a `void*` - cast it to `HMODULE`.

## [1.3.0] - 2026-10-02

### Changed
- **Mods can hook the same function.** A second plugin or script hooking an address another one
  already hooked used to be turned away; now both hooks run, the one loaded last first, and
  `fcse.log` names which went ahead of which. This applies to `Hook` and `MidHook` alike, in any
  mix, and Lua mods get it through `fcse.hook` and `fcse.midhook`. Hooking into the middle of
  another hook's bytes is still refused. `FCSE_API_VERSION` is unchanged and existing plugins keep
  loading.

## [1.2.1] - 2026-10-01

### Fixed
- **A plugin's settings stay under its own heading when its DLL is renamed to lowercase.** Some
  installers lowercase file names, and the Mod Configuration Menu then showed the plugin twice: once
  with no settings, once under the name it registered. It now matches the two ignoring case and
  shows one heading, in the plugin's own casing.

## [1.2.0] - 2026-09-14

### Added
- **Hook anywhere in a function, not just at its entry** — `MidHook(target, handler)` runs a handler
  just before any instruction and hands it every register of that moment, to read or rewrite. Lua
  mods get it as `fcse.midhook(address, handler)`.
- **The Mod Configuration Menu scrolls.** Past the page's seventeen lines it now moves a window over
  its own list of rows, so a mod's settings are no longer cut off. Ported from FC2JackalFix, which
  solved the same limit first.
- **A plugin can grey out a settings row, or keep it off the page** — `FCSE_SettingFlag_Disabled`
  draws it locked and unselectable, value and all; `FCSE_SettingFlag_Hidden` omits it, leaving
  `fcse.ini` as its only interface. Either way the setting keeps working everywhere else. Lua mods
  get both as `fcse.setting{ … disabled = true, hidden = true }`.
- **Formatted log lines for C++ plugins** — `FCSE::Logf(format, ...)` in `fcse_api.h` formats
  printf-style and writes through `Log`, so a plugin no longer sizes a buffer for every message.
  Header-only: `FCSE_API_VERSION` is unchanged and existing plugins keep loading.

### Changed
- Plugin ABI is now `FCSE_API_VERSION` 7: `FCSE_PluginAPI` gained `MidHook` and `FCSE_Setting` a
  `flags` field. Existing plugins need no source change but must be rebuilt.
- Hooks are installed by safetyhook instead of MinHook. Two hooks now conflict only when they
  overlap the bytes each one actually displaces, rather than whenever they sit within five bytes.

## [1.1.0] - 2026-08-23

### Changed
- Internals only — sources regrouped, the settings page split into four, more of FCSE brought under
  test. The plugin ABI, the Lua API and both examples are unchanged, so existing mods keep working.

## [1.0.0] - 2026-08-11

### Added
- **Two ways to write a mod**, loaded from `bin\plugins\` at any depth: a Lua script, or a DLL
  exporting `FCSE_Load`. The same API either way.
- **Hooks, memory patches, byte-signature scanning and engine function calls** — how a mod changes
  engine behaviour without anyone shipping a patched `Dunia.dll`.
- **Settings rows a mod registers into the game's own Options screens**, persisted to `fcse.ini`.
- **An address library keyed by symbol rather than address**, so one build runs on both PC builds of
  v1.03.
- **`bin\fcse.log`** — what each script and plugin registered, hooked or patched, and where two of
  them collide.
