---
title: Functional Outposts (cleared outposts stay empty for 45 minutes)
kind: component
bundle: gameplay
status: located
systems: [missions, ai]
match:
  # the 58 generated outpost scripts (every nameless .lua in the mod)
  - "_hash/*.lua"
  # one mission per outpost, one omni entity hosting its script (generated names are 7 characters)
  - "worlds/*/generated/world*.game.xml/missions/outposts/**"
  - "worlds/*/generated/world*.omnis.fcb/dominoomnientity_???????.*.xml"
  # the guards' new mission layers: sector layout (what gates spawning) and each entity's filing
  - "levels/*/generated/worldsectors/worldsector*.data.fcb/_layout.xml#layer[missions\\outposts\\*]{,/**}"
  - "levels/*/generated/worldsectors/worldsector*.data.fcb/_layout.xml#remove[*]"
  - "levels/*/generated/worldsectors/worldsector*.data.fcb/*#Components/CMissionComponent"
  - "levels/*/generated/worldsectors/worldsector*.data.fcb/*#Components/CMissionComponent/{hidMissionLayerPath,text_hidMissionLayerPath}"
exclude: []
requires: []
verified: diff
---

# Functional Outposts (cleared outposts stay empty for 45 minutes)

Each guarded outpost becomes its own respawn group: once every guard of an outpost is dead, the
outpost stays empty for 45 minutes of play, then the whole group spawns again. This is scubrah's
"Functional Outposts" v1.0 folded in; every script is headed `-- Generated for "Functional
Outposts" v1.0`. The mod's optional customisation "Outpost Respawn Timer" offers 30, 45 or 60
minutes; this variant ships the 45-minute scripts.

## How

The world and sector data are the same as Scubrah's Patch's
[Functional Outposts](../../scubrahs-patch/features/functional-outposts.md), file for file:

- **58 missions and 58 omni entities.** `Missions/Outposts/<cell>/<name>` in `world1.game.xml` (32)
  and `world2.game.xml` (26), each owning one layer `missions\outposts\<cell>\<name>`, active by
  default; 58 `DominoOmniEntity_<name>` in `world*.omnis.fcb` run
  `domino\User\Outpost_V2\<cell>-<name>.lua`, stored nameless as the 58 `_hash/*.lua`. The missions
  and omni entities are byte-identical to Scubrah's Patch's.
- **Guards moved into those layers.** 304 sector `_layout.xml` `layer[missions\outposts\...]`
  entity ops, 126 entities refiled (`hidMissionLayerPath` and its `text_` twin), 178 entities given a
  `CMissionComponent`, and 30 `remove[...]` ops dropping the old layers a move emptied
  (`_disableformission` `pgp_ai`, `w1d4_a09centralroad_enemiesstp`, `w2b4_a17_enemies`,
  `a21_bridgearmsbazaar_ai`; `convoy_0N_disable`; `a1lm01\misnbase_patrol_hide`; `a2sm05_ai_disable`;
  `ubidays\5a_ai_minepgp_01`). Identical to Scubrah's Patch's.
- **The scripts.** Each watches every guard with a `HealthEvents` box and counts kills; a
  `StatusListener` on one guard resets the count when the group spawns or is removed. At the full
  count it disables the outpost's mission through the stock `SetMissionState` box, then starts the
  stock `Delay` box with `Seconds = 2700`; when that elapses it enables the mission again.

## Differs from Scubrah's Patch

All 58 scripts differ from Scubrah's Patch's in two lines:

- the delay is the literal `2700` instead of the named preset `"Outpost"`, so it needs no modified
  `Delay` box ([`outposts-delay-presets`](../../scubrahs-patch/features/outposts-delay-presets.md))
  and no `OutpostDelay` global, and the respawn time is changed only by swapping the scripts;
- there is no "Outpost Cleared" popup (no `PushNewObjective` call). The mod still ships the popup's
  text `SafeHouse/PGPUnlockedSwoosh` in nine languages and an `Objective[PGPUnlocked]` popup in
  `onscreendata.xml` (not on this page), which nothing in the mod shows.

So this page is self-contained: stock Domino boxes only, no globals.

## Uncertain

- The town guard post `w1_c_3\zXmOLgx` holds the guards of vanilla's `_disableformission\pgp_ai`
  layer, which the tutorial, `master_world1` and the taxi-ride scripts switch off during the
  opening. Scubrah's Patch switches the outpost off at game start and back on after the intro;
  this mod has no such hunk, so those guards are present from a new game on (inference from the
  vanilla scripts, not checked in game).
- Moving guards out of the `_disableformission`, convoy, library and story layers takes them out of
  reach of the vanilla scripts that empty those areas for a mission (A1LM01, A1LM04, A2SM05, convoy
  missions), as in Scubrah's Patch.
