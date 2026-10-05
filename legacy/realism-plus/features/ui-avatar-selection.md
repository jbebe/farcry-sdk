---
title: Four more characters on the start screen
kind: component
bundle: gameplay
status: located
systems: [ui, player]
match:
  - "ui/localized/*/*/ui/sp_avatar.mgb.desc#**"
  - "ui/textures/avatars/avatar_bai.xbt"
  - "languages/*/oasisstrings.fragment.xml#MainMenu/STORYMODE_AVATAR_*"
exclude: []
requires: [graphics-avatar-models, player-playable-characters]
verified: diff
---

# Four more characters on the start screen

The new-game character choice offers thirteen mercenaries instead of nine: the buddies Flora
Guillen, Michele Dachss and Nasreen Davar, and an "Anonymous Mercenary".

## How

- **The list.** `ui/localized/<pc|pcwidescreen>/<eng|fre|ger|ita|spa>/ui/sp_avatar.mgb.desc`
  (10 files), `STORY_AVATAR_SELECTION_PAGE/avatar_list`, gains four `avatar` entries after Xianyong
  Bai: `buddyName` `Flora_Guillen`, `Michele_Dachss`, `Nasreen_Davar`, `Anonymous_Mercenary`, each
  with a `displayName` and a `text` key `STORYMODE_AVATAR_FLORA` / `_MICHELE` / `_NASREEN` /
  `_ANONYMOUS`. The page layout `sp_avatar.mgb` and the list of nine avatar textures in the
  `.desc`'s dependencies are unchanged.
- **The bios.** Those four keys are new in all ten languages' string tables: age, nationality,
  birthplace, height, weight, hair, eyes and experience in the style of the original nine (Flora 39,
  Cuban-Angolan, ex-Angolan Army and MPLA; Michele 35, French, ex-Gendarmerie; Nasreen 29, Tajik;
  the Anonymous Mercenary all `???`, in English with "All we know is they're here for the Jackal.").
  In English only, the nine existing bios are recapitalised (`Hair: brown` -> `Hair: Brown`, and so
  on).
- **The photo.** `ui/textures/avatars/avatar_bai.xbt`, Xianyong Bai's photo, 512x512 -> 1024x1024,
  becomes a 2x2 grid of four labelled snapshots: XIANYONG, FLORA, MICHELE, NASREEN.

## Depends on

- `graphics-avatar-models`, the four new body models.
- `player-playable-characters`: the player archetypes `MainCharacter.PawnPlayer.Flora_Guillen`,
  `Michele_Dachss`, `Nasreen_Davar` and the new `Anonymous_Mercenary`, and their `PawnArchetype`
  entries in `engine/gamemodes/gamemodesconfig.xml`. Without them the new entries name characters
  the game cannot spawn.

## Uncertain

- Only the English, French, German, Italian and Spanish UI packages are shipped; with the Czech,
  Hungarian, Polish or Russian interface the list presumably stays at nine.
- How the page picks a photo for the four new entries is not traced. The collage in Bai's photo
  suggests the page shows the last texture for every entry from Bai on, so Bai himself now shows the
  collage too (inference); the Anonymous Mercenary has no photo of his own.
