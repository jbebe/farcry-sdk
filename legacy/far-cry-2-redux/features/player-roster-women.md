---
title: Flora, Michele and Nasreen as playable characters
kind: component
bundle: gameplay
claims:
  - "Added Flora, Michele, and Nasreen to roster"
status: located
systems: [player, graphics, audio]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer/{flora_guillen,michele_dachss,nasreen_davar}.xml#**/{CGraphicComponent,CPlayerSoundAndFXComponent/PostFXSounds}/**"
  - "_hash/{3b3efb2b,bf8b4f9a,24da6624}.xbg"
exclude: []
requires: [ui-avatar-selection]
verified: diff
---

# Flora, Michele and Nasreen as playable characters

The buddies Flora Guillen, Michele Dachss and Nasreen Davar can be played, each with her own
first-person body instead of Warren's, and without a man's pain grunts.

## How

**Archetypes.** The base game already lists `player.MainCharacter.PawnPlayer.Flora_Guillen`,
`Michele_Dachss` and `Nasreen_Davar`, but their first-person body (`CGraphicComponent/object[1]/objModel`)
is `graphics\characters\buddies\warren\warren_avatar.xbg` (`1519F107`). The mod's copies in
`generated/entitylibrarypatchoverride.fcb` point it at `graphics\actors\flora.xbg` (`3B3EFB2B`),
`graphics\actors\michele.xbg` (`BF8B4F9A`) and `graphics\actors\nasreen.xbg` (`24DA6624`), with the
`text_objModel` twins; set `CGraphicComponent/bAlwaysShowInReflection` `False` -> `True`; and clear
pain sounds in `CPlayerSoundAndFXComponent/PostFXSounds` to `0xFFFFFFFF`: the four
`sndPierce{Head,Front,Left,Right}StimSound` (`0x004BF6C3`) on all three, plus `sndBurnStimSound` and
`sndCrushStimSound` (`0x004BF6C4`) on Michele and Nasreen (16 values).

**Models.** The three new bodies ship nameless in the patch archive, named here by the hashes the
archetypes use:

| File | Name | Copy of (base `worlds.dat`) |
|---|---|---|
| `_hash/3b3efb2b.xbg` | `graphics\actors\flora.xbg` | `graphics\actors\buddy_floraguillen\floraguillen.xbg`, 4 bytes changed |
| `_hash/bf8b4f9a.xbg` | `graphics\actors\michele.xbg` | `...\buddy_micheledachss\micheledachss.xbg`, 6 bytes changed |
| `_hash/24da6624.xbg` | `graphics\actors\nasreen.xbg` | `...\buddy_nasreendavar\nasreendavar.xbg`, 4 bytes changed |

Each changed byte turns a material path's `.xbm` into `.x.m`, so those parts find no material and
are not drawn: the full buddy model becomes a headless body for first person. The broken materials
are the head's: `EYE_TEETH` and `EDON` on all three, plus `FLORA_HAIR` and `FLORA_FACE`;
`MICHELE_HAIR`, `MICHELE_HAIRALPHABLEND`, `MICHELE_HEAD` and `MICHELE_HEADBAND`; `NASREEN_HAIR_01_ASCOPY`
and `NASREEN_HEAD_M`. The author's guide describes the method
([player character](../../../docs/docs/modding/guide/player-character.md#guide---adding-the-female-mercenaries-as-playable-characters)).

## Depends on

- `ui-avatar-selection`: the start screen lists the three women (`sp_avatar.mgb.desc`) with their
  biographies; without it they cannot be chosen.
- `player-clothing-camo-textures` repaints Flora's trousers and both women's holsters.
- Realism Plus does the same with differently made models
  ([`player-playable-characters`](../../realism-plus/features/player-playable-characters.md)).

## Uncertain

- Which parts the broken material paths hide is read from the material names; the models are not
  rendered here. `bAlwaysShowInReflection` presumably shows the body in water and mirror reflections.
- The mod also replaces `warren.xbg` and `warren_avatar.xbg` and adds a `michele_avatar.xbg`, which
  nothing in single player loads (`noise-weapons-unused-files`).
