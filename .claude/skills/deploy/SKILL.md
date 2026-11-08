---
name: deploy
description: Use when a mod the user is play-testing has changed and has to go into the game install for them to play it - infers which layers are already deployed by content, adds this mod to that set in one rebuild, and checks the new patch against the old
---

# Deploy

After a change to a mod the user is play-testing, put it into the game yourself and say in the
report that it is deployed. Never hand the user the install command. Launching the game is not part
of a deploy; `CLAUDE.md`'s live-testing rule covers it.

The install is the GOG one, `C:\Games\Far Cry 2`, unless the user says Steam
(`C:\Program Files (x86)\Steam\steamapps\common\Far Cry 2`). Other sessions deploy into it too.
`jackall-cli mod build` rebuilds the patch from vanilla plus only the layers it is given, so a layer
left off is removed, and nothing records which layers built the current patch. Read the set from the
install every time, never from memory or an earlier build.

Commands run from the repo root. The CLI is
`tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe`; if it is missing, build it with
`dotnet build tools\JackAll\src\JackAll.Cli -c Release`. Make a new folder in the scratchpad for this
deploy, `<work>` below.

## 1. Check

- The game must be closed: no `FarCry2` or `FCSE` process.
- If the newest folder under `C:\Games\Far Cry 2 - perftest\` has `"restored": false` in its
  `run.json`, a perftest holds the install. Ask the user before going on.
- `git status -- mods/`. A layer deploys as it is on disk, so another mod's uncommitted changes would
  go in with this one. Ask the user, or deploy that layer from a copy in `<work>` holding what is
  installed now.

## 2. Build the mod

- Run its `build.ps1` with no arguments, plus any layer step its README names (`build-layer.ps1`,
  `build_layer.py`, DevTools' `range\build-layer.ps1`).
- Sky Overhaul's `build.ps1` stages its DLL into the layer only under `-Install`. Copy
  `mods\sky-overhaul\out\build\x86-release\SkyOverhaul.dll` into `mods\sky-overhaul\layer\plugins\`
  yourself.
- **`-Install` does a different thing for each kind of mod. Never use the first row's while other
  layers are deployed**; the other rows only copy files, as step 3 does by hand.

  | `build.ps1 -Install` | Mods |
  | --- | --- |
  | Rebuilds the patch from vanilla plus this one layer, dropping every other | aiming-overhaul, flashlight, sky-overhaul, vehicle-overhaul, wildlife-overhaul |
  | Copies the DLL into `bin\plugins\` and leaves the patch alone | UFCP, DevTools, difficulty-overhaul, inventory-overhaul, legacy-plugin |
  | Copies `layer\plugins\sound-overhaul\` into `bin\plugins\sound-overhaul\` | sound-overhaul |
  | Not offered | map-overhaul, third-person |

- Log every build to a file in `<work>` (`... *> "<work>\build.log"`) and read its end with
  `Get-Content -Tail`. Never pipe a build into `Select-Object -First` or `head`: that stops it early.

## 3. A change to the DLL alone

Copy the DLL over the installed one and go to step 7. The patch is not rebuilt.

- A staged plugin goes to the same path under `bin\plugins\`:
  `mods\aiming-overhaul\layer\plugins\aiming-overhaul\AimingOverhaul.dll` to
  `bin\plugins\aiming-overhaul\AimingOverhaul.dll`.
- DevTools and Legacy Plugin stage nothing. Copy `out\build\x86-release\DevTools.dll` or
  `LegacyPlugin.dll` into `bin\plugins\`.

## 4. Infer the installed set

1. Back up: copy `Data_Win32\patch.fat` and `patch.dat` into `<work>\before\`.
2. Extract the copy: `jackall-cli archive extract "<work>\before\patch.fat" --names -o "<work>\before-x"`.
   Never extract an archive inside the install: without `-o` the files land beside it.
3. Hash every extracted file and every whole file in each `mods\<mod>\layer\mods\`. A fragment is not
   a whole file: it sits in a folder named after the file it merges into (`entitylibrary.fcb\`,
   `hud.mgb\`, ...) or is an `oasisstrings.fragment.xml`.
4. A layer is deployed when its whole files are found by content. Match on content, not on names:
   entries without a recovered name come out under `_unknown\`. A layer found only in part was
   deployed from an older state of it: tell the user.
5. A layer with no whole files shows by its plugin in `bin\plugins\.jackall-plugins.json`, matched by
   `path` (every folder layer's `layer` field reads `layer`), or by a value its fragments set, read
   in the extracted container.
6. Anything in `bin\plugins\` the manifest does not list was copied by hand, DevTools.dll among them,
   and `mod build` leaves it alone. The last `plugin '<name>' loaded` lines in `bin\fcse.log` show
   what the game loaded last.
7. Each layer's `layer\plugins\` files must be byte-identical to the installed ones, so the rebuild
   swaps no plugin. If one differs, find out why before going on.

## 5. Rebuild

```
jackall-cli mod build --game "C:\Games\Far Cry 2" --layer mods\<A>\layer --layer mods\<B>\layer ... --layer mods\<mod>\layer *> "<work>\deploy.log"
```

- The whole set plus this mod, in one build.
- A later `--layer` wins a whole file two layers ship. `vehicle-overhaul` goes after `sound-overhaul`
  (the Datsun's idle bank), `vss-vintorez` after `sound-overhaul` and `aiming-overhaul` (its shot
  banks, the Dart Rifle's fields), and `difficulty-overhaul` after `DevTools` (the test range's
  particle library). Otherwise the mod under test goes last.
- Under Windows PowerShell the CLI's progress on stderr stops a script running with
  `$ErrorActionPreference = 'Stop'`. Run it under `Continue` and check `$LASTEXITCODE`, as `Invoke-Cli`
  in `.claude\skills\perftest\perftest.ps1` does.
- Read the log's end: `Built ... entries`, then `bin\plugins: N plugin file(s) deployed, M removed`,
  then any `Warning:` lines. M is 0 unless a plugin was meant to go. A removal nobody asked for means
  a layer was left out: copy `<work>\before\` back into `Data_Win32`.

## 6. Diff by content

1. Copy the new `patch.fat` and `patch.dat` into `<work>\after\` and extract them to `<work>\after-x`
   the same way.
2. Compare the two by content: files added, removed and changed.
3. Only this mod's files and the containers its fragments merge into may change. Two changes come on
   every build of the same inputs and are not regressions:
   - six `.rml` entries of about 1 MB differ by some 100 bytes of floats;
   - each `languages\*\oasisstrings.rml` keeps its size with its lines reordered, which
     `jackall-cli rml decode` shows as the same lines.
4. Anything else changed or removed: copy `<work>\before\patch.fat` and `patch.dat` back into
   `Data_Win32` and find out why before building again.

## 7. Report

Say it is deployed: the layers in order, the plugins line, and the diff in brief. Leave `<work>` in
place; its `before\` is the way back.
