# DevTools

**Before writing or editing any C++, header, or CMake file in this tree, invoke the
`implementation` skill** (`.claude/skills/implementation/SKILL.md`). DevTools is a separate CMake
project from `tools/FCSE/`, but it is the same codebase stylistically and is held to exactly the
same rules - formatting, naming, comment voice, failure semantics and address discipline all apply
here unchanged. `tools/FCSE/README.md` describes the loader this plugin runs on.

## What this is

The developer side of Far Cry 2, made reachable: the parts of the engine the shipped build still
carries but closes off, opened again, applied to the running game rather than to files on disk.
`README.md` documents each feature.

## Rules specific to this tree

**Same layout and the same rules as `mods/UFCP/CLAUDE.md`** - `src/fixes/` and `src/options/`, one
feature per file, `FCSE::Pattern` for interior sites, a feature that cannot find its site logs and
returns, and each file documents the behaviour rather than just the patch. Read that file; only what
differs is below.

- **Four folders UFCP does not have.** `src/engine/` is something the running game owns - a
  singleton, a call, the frame, the device, the window - wrapped so the rest of the plugin can reach
  it. `src/commands/` is data describing what to ask for. `src/overlay/` is the UI drawn on the
  game's frames, and `src/remote/` the same reach for a program, over a pipe. **Nothing in
  `src/engine/` or `src/commands/` is configurable**: they are called, not configured.
- **Two ways of finding a site, and which one depends on whose code it is.** Dunia's own code is
  found by `FCSE::Pattern` or the address library, and `scripts/verify_patterns.py` checks it.
  Direct3D and DirectInput are not Dunia's: those live in `d3d9.dll` and `dinput8.dll`, are shared by
  every object in the process, and are found by vtable index off an object built for the purpose. The
  verifier cannot see those, and should not be expected to.
- **Dear ImGui is the one exception to "`fcse_api.h` is the entire dependency".** It is pinned once,
  in `include/devtools_imgui.cmake`, fetched at configure time and compiled into the plugin, never
  linked from `tools/FCSE/`. The rule it does not weaken: nothing here ever reaches into
  `tools/FCSE/src/`.
- **`include/` is the only part of this tree another plugin may use.** `devtools_api.h` is the
  overlay's contract and `devtools_imgui.cmake` the Dear ImGui both sides build. Any change to the
  header's structs or signatures bumps `DEVTOOLS_OVERLAY_API_VERSION`, and moving the pin refuses
  every other plugin's window until that plugin is rebuilt. A plugin knows DevTools only through this
  folder, and runs unchanged without it.

- **A feature belongs here when it is only useful while working on the game.** That is the test, and
  it is about who wants the feature, not whether it is a fix or an option - the `-load` fix is an
  unconditional bug fix and still belongs here, because iterating on a save is not something a
  player does. Anything a player wants goes in `mods/UFCP/`. The two are separate plugins so either
  can be installed without the other, and neither knows the other exists.
- **Options are registered under `[DevTools]`** in `bin\fcse.ini` and appear on DevTools' page in the
  Mod Configuration Menu. FCSE owns the stored value, which is what makes a setting survive a
  relaunch - the plugin never holds a pointer to it, and the ABI offers no way to write one back, so
  **the Mod Configuration Menu is the only thing that can change a stored value**.
- **UFCP's "every default leaves the game exactly as it shipped" does not hold here, deliberately.**
  The developer console, the FPS readout, the launch-time skip, fast loading and the free camera's
  key are on out of the box, because they are what this plugin is installed for and switching them on
  by hand every time is friction with no upside. **Anything that changes how the game plays still
  defaults to off** - god mode, ammo, weapons, diamonds - so installing DevTools never silently
  alters a playthrough. A new option needs a deliberate answer to which of those two it is.
- **One option per file in `src/options/`, exposed as a `Get<Name>()`/`Set<Name>(int)` pair** and
  listed once in `src/options/options.cpp`. That table is the single description of an option:
  `src/main.cpp` builds the FCSE rows from it, so a setting is never described twice and the two
  cannot disagree. Every value is an int - a toggle is 0 or 1, a slider its own range, a key an index
  into `Keys::ChoiceLabels`. `Set` returns early when the value is unchanged, because whatever drives
  it may call it every frame.

## Build

```
.\build.ps1                  # x86 release -> DevTools.dll
.\build.ps1 -Config debug
.\build.ps1 -Install "<game>\bin"    # copies into bin\plugins\
```
