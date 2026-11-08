# FCSE (Far Cry Script Extender)

**Before writing or editing any C++, header, Lua or CMake file in this tree, invoke the
`implementation` skill** (`.claude/skills/implementation/SKILL.md`).

## What this is

The plugin loader every mod's DLL runs on: `FCSE.exe` starts the game in place of `FarCry2.exe` and
loads `bin\plugins\`. `README.md` covers how it works, the plugin API and the build.

## Rules specific to this tree

- **Loader work stays inside `tools/FCSE/` and the docs.** Never edit, build or rebuild anything
  under `mods/` unless the user asks for it in that session. The plugins often hold the user's work
  in progress.
- **Never copy build output into a game install unless the user asks**, `build.ps1 -Install`
  included. The install often holds a test the user is in the middle of.
- **An ABI change is allowed; its consequences go to the user, not into action.** Every plugin's
  `FCSE_Load` refuses to run unless the loader's `FCSE_API_VERSION` equals the one it was built
  against, so a bump stops every plugin under `mods/` until it is rebuilt. Name the plugins that need
  a rebuild, and leave rebuilding and deploying them to the user.
- **Steps to check a change in game are written for the user**, as what to look at when they deploy.

## Build

```
.\build.ps1                 # x86 release -> out\build\x86-release\FCSE.exe
.\build.ps1 -Config debug
.\build.ps1 -Tests          # also runs the GoogleTest and Lua suites
.\verify_build.ps1          # the static CRT and embedded resources of the built FCSE.exe
```

`.\build.ps1 -Install "<game>\bin"` copies `FCSE.exe` into the game, and only when the user asks.
