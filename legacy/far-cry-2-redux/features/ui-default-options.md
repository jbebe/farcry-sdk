---
title: New default options
kind: component
bundle: ui
claims: []
status: located
systems: [ui, audio, input]
match:
  - "engine/settings/defaultgameconfig.xml#@{Gamepad_vibration,UseSubtitles,UseAimingHelpers,SkipIntroMovies}"
  - "engine/settings/defaultsoundconfig.xml#@MusicEnabled"
exclude: []
requires: []
existing: mods/UFCP — Skip intro videos option (SkipIntroMovies only)
verified: diff
---

# New default options

A new profile starts without subtitles, music, gamepad vibration or aim assist, and skips the intro
videos. Not a line of the readme.

## How

The root `Profile` attributes of the files a new profile is made from:

- `engine/settings/defaultgameconfig.xml`: `Gamepad_vibration` 1 -> 0, `UseSubtitles` 1 -> 0,
  `UseAimingHelpers` 1 -> 0, and a new `SkipIntroMovies` 1.
- `engine/settings/defaultsoundconfig.xml`: `MusicEnabled` 1 -> 0.

These are defaults; each is still an option in the menus (the intro skip on the command line as
`-GameProfile_SkipIntroMovies`, which the bundled Multi-Fixer also offers).

The same file's `DifficultyLevel` is `ui-default-difficulty`.

## Uncertain

- `Gamepad_vibration` and `UseAimingHelpers` do not appear as strings in Steam's `Dunia.dll`
  (`Vibration`, `UseSubtitles`, `SkipIntroMovies` and `MusicEnabled` do), so those two keys may be
  read under another name or not at all; not traced.
- A profile already saved keeps its own values; these apply to a new one.
