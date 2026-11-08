# UFCP (Unofficial Far Cry Patch)

**Before writing or editing any C++, header, or CMake file in this tree, invoke the
`implementation` skill** (`.claude/skills/implementation/SKILL.md`). UFCP is a separate CMake
project from `tools/FCSE/`, but it is the same codebase stylistically and is held to exactly the
same rules - formatting, naming, comment voice, failure semantics and address discipline all apply
here unchanged. `tools/FCSE/README.md` describes the loader this plugin runs on.

## What this is

Fixes for bugs Ubisoft never patched, applied to the running game rather than to files on disk:
nothing is overwritten, nothing survives uninstalling the plugin, and it coexists with the data
mods players already have. A bug that is wrong data rather than wrong code is fixed in `layer/` as
a JackAll fragment, which merges with other mods' data; the plugin ships inside that layer, staged
by `build.ps1`. `README.md` documents each fix.

## Rules specific to this tree

- **UFCP is for players; `mods/DevTools/` is for modders and reverse engineers.** A feature only
  someone working on the game wants belongs there, not here. The two are separate plugins so either
  can be installed without the other, and neither knows the other exists.
- **`tools/FCSE/include/fcse_api.h` is the entire dependency.** Never reach into `tools/FCSE/src/`
  or link against anything in it. If a fix needs something the public ABI doesn't offer, that is a
  change to the ABI (and to FCSE), not an include path.
- **Two categories, and the split is the organising principle.** `src/fixes/` is wrong behaviour
  corrected, applied unconditionally with no setting, because a fix that needs a switch is a
  preference in disguise. `src/options/` is preferences, where the right answer depends on the
  player or their hardware - one `RegisterSettings` row each under `[UFCP]` in `bin\fcse.ini`,
  defaulting to leaving the game exactly as it shipped. One row deliberately breaks that: a 60 Hz
  frame cap is on by default, being what someone installing a patch is asking for. A second
  exception is a decision to take, not a default to reach for.
  `src/main.cpp` states this contract; keep it and this file in step. Both kinds report what they
  did to `bin\fcse.log`.
- **`src/engine/` is for seams shared by more than one feature**, and never carries a setting of its
  own. `command_line` owns the single `RunGame` detour, `memory_probe` answers whether an engine
  pointer is still readable and whether it is Dunia's, and `render_profile` finds the block the
  engine's graphics settings live in. The directory exists so features share one answer to each
  question, and because FCSE matches patterns against live code: a second feature pattern-matching
  an entry another has hooked would find the first one's hook jump there instead.
  These are the only files in the tree with headers.
- **One fix per file** in `src/fixes/`, each finding its own site and applying itself, or logging
  why it could not. Adding one means writing `src/fixes/<name>.cpp`, declaring the single
  `ApplyXxxFix()` entry point in `src/main.cpp` with a one-line comment naming the symptom, calling
  it from `FCSE_Load`, and adding the file to `CMakeLists.txt`.
- **One option per file** in `src/options/`, the same way: write `src/options/<name>.cpp`, declare
  its `On<Name>Changed` callback in `src/main.cpp`, add an `FCSE_Setting` row whose default leaves
  the game as it shipped, and add the file to `CMakeLists.txt`. An option that hooks rather than
  patches also needs an `InstallXxxHook()` called before `RegisterSettings`, because registration is
  what delivers the saved value.
- **Address interior sites with `FCSE::Pattern`, never `FCSE::Uplay`/`Retail`.** The address library
  is keyed by exact entries - function starts and data addresses - so a branch byte halfway down a
  function resolves to nothing and the feature silently does nothing on every build. Patterns are
  also the only thing `scripts/verify_patterns.py` can check.
- **A fix that cannot find its site is not an error.** Log that the build was not patched and
  return; every other fix still applies and the game still runs.
- **Document the bug, not just the patch.** Each fix file opens with the symptom as players report
  it, the root cause traced in the disassembly with addresses for both shipped builds, and what the
  patched bytes change. This is the only place several of these root causes are written down.

## Build

```
.\build.ps1                  # x86 release -> UFCP.dll
.\build.ps1 -Config debug
.\build.ps1 -Install "<game>\bin"    # copies into bin\plugins\
```
