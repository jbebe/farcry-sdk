# Sound Overhaul

Makes Far Cry 2 sound closer to a serious shooter: rooms that ring like rooms, walls that muffle,
gunshots with echoes, and distant fire that cracks.

- **The game's own reverb, back.** Far Cry 2 authored reverb for its buildings, jungle, savannah and
  desert, but it only plays on an EAX sound card, which a modern PC does not have. The bundled DSOAL
  provides one in software, and the reverb changes as you move between places.
- **Every building's reverb fits it**, chosen by its type and size, and shots in a small room slap
  off its walls.
- **Walls muffle by their material.** Open doors and windows let the outside in, and your own
  pickups in a safehouse are no longer muffled.
- **New gunshots** for every single-player weapon, first and third person, from Dark Signal Weapon
  Soundscape, with full-auto playing every round.
- **Echoes** after every unsuppressed shot, big or small by calibre, for enemies' shots too. From
  about 40 m an enemy's shot turns into its crack, and past 80 m the crack is all you hear.
- **New grenade and rocket explosions.**
- **Voices fall off like real speech**, gone by 50 m.
- **No distant dogs and jackals** in the ambience, and a rougher, quieter idle for the Datsun.
- **A Combat music setting**, in the **Mod Configuration** menu and `bin\fcse.ini`. With it off,
  fights play without music, so enemies can be heard.

With DevTools installed, its overlay has a Sound Overhaul window that mutes single shots, full-auto
fire or echoes, to hear the rest of a fight on its own.

## Requirements

- **FCSE 1.4.0 or later**, on Far Cry 2 1.03 (Steam or GOG).

## Installing

This archive is a JackAll layer: `mods\` at its root is game data, `plugins\` is the FCSE plugin with
the DSOAL it loads, and both installers read that same shape.

- **Vortex** — with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll** — add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer sound-overhaul.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall, and removes the plugin too. DSOAL is never copied into `bin\`; the
plugin loads it from its own folder.

## Compatibility

- **A `dsound.dll` already in `bin\`**, such as a separately installed DSOAL, stays in charge, and the
  bundled one is not loaded.
- **Weapon and sound mods** that replace the same weapons' sounds, the explosions, the voice falloff
  or the reverb presets conflict. Whichever loads last wins.

## Credits

- Gunshots, echoes and explosions: **Dark Signal Weapon Soundscape** (DSWS), by Dark Signal's original
  producer, with assets from **JSRS**.
- **DSOAL** and **OpenAL Soft**, by Chris Robinson (kcat), under the LGPL-2.1. Their licenses and exact
  versions are in `plugins\sound-overhaul\dsoal\Documentation\`.
