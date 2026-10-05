---
title: Flora, Michele and Nasreen on the start screen
kind: component
bundle: gameplay
claims:
  - "Added Flora, Michele, and Nasreen to roster"
status: located
systems: [ui, player]
match:
  - "ui/localized/*/*/ui/sp_avatar.mgb.desc"
  - "ui/localized/*/*/ui/sp_avatar.mgb.desc#**"
  - "ui/textures/avatars/avatar_bai.xbt"
  - "languages/english/oasisstrings.fragment.xml#MainMenu/STORYMODE_AVATAR_*"
exclude: []
requires: [player-roster-women]
verified: diff
---

# Flora, Michele and Nasreen on the start screen

The new-game character choice offers twelve mercenaries instead of nine: the buddies Michele Dachss,
Flora Guillen and Nasreen Davar join the list after Xianyong Bai.

## How

- **The list.** One `sp_avatar.mgb.desc` (2,884 bytes) is shipped in all 18 interface folders
  (`ui/localized/<pc|pcwidescreen>/<language>/ui/`). It is the base `pcwidescreen/eng` descriptor
  with three `avatar` entries added to `STORY_AVATAR_SELECTION_PAGE/avatar_list`: `buddyName`
  `Michele_Dachss`, `Flora_Guillen`, `Nasreen_Davar`, `displayName` the full name, `text`
  `STORYMODE_AVATAR_MICHELE` / `_FLORA` / `_NASREEN`. Being one file, its dependencies name the
  `pcwidescreen\eng` layout, fonts and common package, so every language and aspect now loads the
  English widescreen page (8 dependency changes in each of the other ten folders that had a
  descriptor). Czech, Hungarian, Polish and Russian have no descriptor in the base game; the mod adds
  one each (8 new files).
- **The bios.** The three keys are new in the English string table only: age, nationality,
  birthplace, height, weight, hair, eyes and experience in the style of the original nine (Flora
  39, Cuban, experience "Unknown"; Michele 35, French, "Smuggling"; Nasreen 29, Tajik, "Northern
  Alliance"). Other languages show the key.
- **The photo.** `ui/textures/avatars/avatar_bai.xbt`, Xianyong Bai's photo, same 512x512 size,
  becomes a collage: Bai labelled "BAI", with three labelled snapshots of Nasreen, Michele and
  Flora pasted over the lower half.

## Depends on

- The player archetypes `player.MainCharacter.PawnPlayer.Flora_Guillen`, `Michele_Dachss` and
  `Nasreen_Davar` in `generated/entitylibrarypatchoverride.fcb/player/`, whose model the mod changes
  from the base game's stand-in `warren_avatar.xbg` to `graphics\actors\flora.xbg`, `michele.xbg`
  and `nasreen.xbg`, and those models. They are on the player pages; without them the new entries
  play with Warren Clyde's body.

## Uncertain

- How the page picks a photo for entries past the ninth is not traced. The collage in Bai's photo
  suggests the last texture is reused for every entry from Bai on (inference).
- In the non-English interfaces the three new entries show their string keys.
- `legacy/realism-plus` `ui-avatar-selection` adds the same three (plus an anonymous mercenary)
  with its own files and per-language descriptors.
