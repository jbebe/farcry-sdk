---
slug: /cli-mods
sidebar_position: 2
title: Manage mods from the command line
description: Check an install, inspect a mod, build and restore Far Cry 2's patch with jackall-cli's mod commands, with real transcripts
---

# Manage mods from the command line

`jackall-cli.exe` runs the same code as the **Mods** tab, without a window, for build scripts and
other tools. It's a separate download, `jackall-cli-<version>.zip`. Every command below takes your
install folder as `--game`.

The commands are run from a folder that holds the mods' zips. The output is what they printed.

:::tip[Confirmed in game]
VSS Vintorez and the Flashlight mod are installed with `mod build` while they're being made, and
both were played.
:::

## Check the install

```
> jackall-cli mod status --game "C:\Games\Far Cry 2"
Far Cry 2 at C:\Games\Far Cry 2
  patch.fat entries : 215
  vanilla backup    : present
```

The vanilla backup is the copy of the game's own `patch.dat` that every build starts from. JackAll
makes it on the first build.

## Look inside a mod

`mod inspect` says what a zip or folder is before you build it. A normal mod is a layer:

```
> jackall-cli mod inspect vss-vintorez-1.1.0.zip --game "C:\Games\Far Cry 2"
Mod layer (52 file(s))
  root              : <top level>
  file overrides    : 13 (0 hash-addressed)
  .fcb fragments    : 27
  plugin files      : 0 (deployed to bin\plugins)
  not in the game   : 2 (files this mod adds, or a misread root)
  ignored files     : 12 (readmes and the like)
```

**File overrides** replace a whole game file, **.fcb fragments** change one entry of a game file and
merge with other mods. A mod with an FCSE plugin counts it under **plugin files**:

```
> jackall-cli mod inspect flashlight-1.1.1.zip --game "C:\Games\Far Cry 2"
Mod layer (67 file(s))
  root              : <top level>
  file overrides    : 13 (0 hash-addressed)
  .fcb fragments    : 42
  plugin files      : 1 (deployed to bin\plugins)
  not in the game   : 3 (files this mod adds, or a misread root)
  ignored files     : 11 (readmes and the like)
```

A mod that ships its own `patch.dat` isn't a layer. It has to be converted first; see
[Import an old patch.dat mod](/jackall/legacy-import).

```
> jackall-cli mod inspect "FC2 Skip Intro-320-1-1-1651917084.zip" --game "C:\Games\Far Cry 2"
Legacy full-patch mod - a whole replacement patch.dat/patch.fat pair.
  Far Cry 2/Data_Win32/patch.fat
  Far Cry 2/Data_Win32/patch.dat
Run mod import-legacy to convert it into an ordinary layer.
```

## Build

`mod build` is **Deploy mods**: the base game plus each `--layer`, in order. A layer is a mod's zip
or a folder laid out the same way, and the app's `workspace` folder is one too. When two layers
replace the same file, the later one wins.

```
> jackall-cli mod build --game "C:\Games\Far Cry 2" --layer vss-vintorez-1.1.0.zip --layer flashlight-1.1.1.zip --layer workspace
Built C:\Games\Far Cry 2\Data_Win32\patch.dat - 258 entries (11 overridden, 43 added, 36.6 MB)
bin\plugins: 1 plugin file(s) deployed, 0 removed
```

Plugins go to `bin\plugins`, and a plugin from an earlier build that no layer brings any more is
removed. A build always starts from the vanilla backup, so leaving a layer out of the next build
takes it out of the game.

If the game's `patch.dat` already looks modded and there's no vanilla backup yet, `mod build` stops:
building would make that mod part of the base game. Put the original patch back, for example by
verifying the game's files in Steam or GOG Galaxy. Pass `--force` only if you're sure the patch is
the game's own.

### Two mods that change the same value

Where the app's **Deploy mods** stops with a conflict (see
[Managing mods](/jackall/managing-mods)), the command line builds anyway and keeps the later layer's
value. These two mods set the AK-47's magazine to 40 and to 50:

```
> jackall-cli mod build --game "C:\Games\Far Cry 2" --layer ak47-40-rounds.zip --layer ak47-50-rounds.zip
Built C:\Games\Far Cry 2\Data_Win32\patch.dat - 216 entries (0 overridden, 1 added, 15.8 MB)
bin\plugins: 0 plugin file(s) deployed, 1 removed
Warning: 'ak47-50-rounds' overrode 'ak47-40-rounds' inside 'worlds\world1\generated\entitylibrary.fcb\weaponproperties\primary\ak47.xml' at Entity/Components/CWeaponProperties/CommonProperties/Ammo/iAmmoInClip by load order - their edits genuinely conflicted, so only the higher-priority mod's change survived. Reorder the mods, or hand-merge it in JackAll.App.
```

The build succeeds, so check for the warning in a script.

## Find dead edits

`mod lint` is **Check for dead edits**: it lists archetype edits that a later entity library
overrides, so they change nothing in game. See [Archetypes](/jackall/archetypes).

```
> jackall-cli mod lint --game "C:\Games\Far Cry 2" --layer workspace
No dead archetype edits - every edited archetype is the copy the game reads.
```

## Undo

`mod restore` is **Revert to original**.

```
> jackall-cli mod restore --game "C:\Games\Far Cry 2"
Restored the original patch.dat/patch.fat in C:\Games\Far Cry 2\Data_Win32
```

## In a script

Add `--json` to any `mod` command and it prints exactly one JSON object on stdout. Progress goes to
stderr, and a failure is `{"ok":false,"error":"…"}` with exit code 1.

```
> jackall-cli mod status --game "C:\Games\Far Cry 2" --json
{"ok":true,"gamePath":"C:\\Games\\Far Cry 2","valid":true,"dataDir":"C:\\Games\\Far Cry 2\\Data_Win32","patchFat":"C:\\Games\\Far Cry 2\\Data_Win32\\patch.fat","patchDat":"C:\\Games\\Far Cry 2\\Data_Win32\\patch.dat","hasVanillaBackup":true,"looksModded":false,"patchEntries":215,"needsVanillaConfirmation":false}
```

The Vortex extension runs the same commands through `jackall-mi.exe`, a smaller build;
[Vortex](/docs/modding/vortex) documents their JSON.
