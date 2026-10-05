---
title: Female mercenaries and an anonymous mercenary as playable characters
kind: component
bundle: gameplay
status: located
systems: [player, graphics, audio]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer/{flora_guillen,michele_dachss,nasreen_davar}.xml#**/{CGraphicComponent,CPlayerSoundAndFXComponent}/**"
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer/anonymous_mercenary.xml"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/PlayerService/**"
exclude: []
requires: [graphics-avatar-models, ui-avatar-selection]
verified: diff
---

# Female mercenaries and an anonymous mercenary as playable characters

Flora Guillen, Michele Dachss and Nasreen Davar become proper player characters with their own
bodies, and a new "Anonymous Mercenary" character plays with a randomly assembled mercenary body.

## How

- **The women.** The base game already lists `player.MainCharacter.PawnPlayer.Flora_Guillen`,
  `Michele_Dachss` and `Nasreen_Davar` in `PlayerService`, but their first-person body
  (`CGraphicComponent/object[1]/objModel`) is Warren's `graphics\characters\buddies\warren\warren_avatar.xbg`
  (`1519F107`). The mod's copies in `generated/entitylibrarypatchoverride.fcb` point it at
  `graphics\actors\buddy_floraguillen\floraguillen_avatar.xbg` (`74D38315`),
  `...\buddy_micheledachss\micheledachss_avatar.xbg` (`CAE54B41`) and
  `...\buddy_nasreendavar\nasreendavar_avatar.xbg` (`9DA20FB2`), with the `text_objModel` twins, and
  clear six pain sounds each (`CPlayerSoundAndFXComponent/PostFXSounds/snd{Burn,Crush}StimSound`
  `0x004BF6C4` and `sndPierce{Head,Front,Left,Right}StimSound` `0x004BF6C3` -> `0xFFFFFFFF`), the
  sounds every male character keeps, so presumably a man's grunts (24 values). The three models ship nameless as
  `_hash/74d38315.xbg`, `_hash/cae54b41.xbg` and `_hash/9da20fb2.xbg` (`graphics-avatar-models`); the
  author's guide makes them from the buddies' own models with the head's materials disabled
  ([player character](../../../docs/docs/modding/guide/player-character.md#guide---adding-the-female-mercenaries-as-playable-characters)).
- **The anonymous mercenary.** A new whole archetype, `player.MainCharacter.PawnPlayer.Anonymous_Mercenary`,
  is a copy of a playable character (with this mod's jump, sprint and slope values) that adds a
  `CFileDescriptorComponent` naming the kit `graphics\actors\anonymous_merc\anonymous_merc.xbg`
  (`DCB50936`, shipped as `_hash/dcb50936.xbg`, `graphics-avatar-models`) with a `GraphicKitComponent` part
  list, and a `CGraphicKitComponent` (`bRadomize` `True`, tag `anonymousmerc`, eleven part
  overwrites); its separate avatar body is cleared. Its phone ring tone sounds differ
  (`0x00455CFE`/`0x00455D06`).
- **The list.** `gamemodesconfig.xml` `PlayerService`, in both `Single` and `FCXBenchmark`: the bare
  `player.MainCharacter.PawnPlayer` entry is replaced by `...PawnPlayer.Anonymous_Mercenary`.

## Depends on

- `graphics-avatar-models`: the four body models.
- `ui-avatar-selection`: the character-select menu only offers what `sp_avatar.mgb.desc` lists, and
  the mod adds `Flora_Guillen`, `Michele_Dachss`, `Nasreen_Davar` and `Anonymous_Mercenary` there,
  with their biographies. Without it none of the four can be chosen.

## Uncertain

- What the base `PawnPlayer` entry did in the list, and what removing it changes, is not traced.
- Whether the three avatar models are the guide's head-stripped edits is inferred from the names and
  the guide; the files are not decoded here.
