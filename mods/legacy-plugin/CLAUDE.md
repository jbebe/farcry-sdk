# Legacy Plugin

**Before writing or editing any C++, header, or CMake file in this tree, invoke the
`implementation` skill** (`.claude/skills/implementation/SKILL.md`). Held to the same rules as
`mods/UFCP/`, whose `CLAUDE.md` this follows.

## What this is

The `Dunia.dll` half of community mods' preference tweaks - Scubrah's Patch, Realism Plus,
FC2JackalFix - applied in memory as FCSE settings. Those mods ship their own `patch.dat` data; this
supplies only the engine side. `README.md` lists every tweak and its source.

## Rules specific to this tree

- **`Dunia.dll` only.** No data layer, no `patch.dat` files. A tweak whose effect is data belongs
  in the legacy mod or a data layer, not here.
- **No fixes.** Wrong behaviour corrected belongs in `mods/UFCP/`. Everything here is a preference.
- **Every row defaults to the game as shipped.** Off for a checkbox, the game's own value for a
  choice. Brown filter is on by default because the game ships with it.
- **One tweak per file** in `src/options/`, exporting exactly `Install<X>()` and
  `On<X>Changed(const FCSE_SettingValue*, void*)`. Adding one means writing the file, declaring both
  in `src/main.cpp`, calling `Install<X>()` before `RegisterSettings`, adding its row, and listing
  the file in `CMakeLists.txt`.
- **`tools/FCSE/include/fcse_api.h` is the entire dependency.** Never reach into `tools/FCSE/src/`.
- **Address interior sites with `FCSE::Pattern`**, checked with `scripts/verify_patterns.py`:
  exactly one match on at least one shipped build, never two on any.
- **A tweak that cannot find its site logs it and does nothing.** Every other tweak still applies.
- **Each file header names the source mod and what the patch changes**, and credits FC2JackalFix
  as `FC2JackalFix (MIT, (c) 2026 Joshhhuaaa, TGP482)` with the module it came from.

## Build

```
.\build.ps1                  # x86 release -> LegacyPlugin.dll
.\build.ps1 -Config debug
.\build.ps1 -Install "<game>\bin"    # copies into bin\plugins\
```
