---
title: Fire budget lifted
kind: component
bundle: gameplay
claims:
  - "Fire now spreads much further and burns longer"
status: located
systems: [environment, world]
match:
  - "worlds/*/generated/*.managers.fcb/firemanager.*#**/Description**"
exclude: []
requires: []
verified: diff
---

# Fire budget lifted

Fires stop counting against the engine's fire budget, so more of them can burn at once and a blaze
is not cut back as it grows.

## How

Each world's `FireManager` (`world1.managers.fcb`, `world2.managers.fcb`) holds one stim effect per
burnable kind (`Burnable.Object.HayBundle01`, `Burnable.Region.Savannah`, ...; 54 in each). Each
effect's `Description` is a small compiled RML document whose attribute values sit in a string
table, shared: every attribute whose value is `1` points at the same `"1"` string.

The mod edits those tables byte by byte (read from the raw bytes, not the decoder):

- **One byte, 45 effects per world.** Where the table's `"1"` string sits right after
  `fFireBudgetCostModifier`, that single byte becomes `"0"`. The document is otherwise
  byte-identical. Every attribute sharing the string reads `0`: always `fFireBudgetCostModifier`,
  and per effect also `fReplacementLOD`, `fReplacementSize`, `fMinStartingGravity`,
  `fMinEndingGravity`, `fBurnStimRadius`, `fFearStimRadius`, `fFearStimLevel`, and in a few
  `bBlackSmoke`, `fResolution`, `fEmitterRoaming`, `fBurnStimLevel`.
- **Two bytes, `Burnable.Region.Jungle`.** Its own `fFireBudgetCostModifier` string `"15"` becomes
  `"00"`.
- **Two inserted bytes, 7 effects in `world1`, 3 in `world2`.** Where the effect's `"1"` was stored
  earlier in the table, `"0\0"` is inserted after `fFireBudgetCostModifier` without updating the
  header's table size or any offset (`Burnable.Tree.bush`, `Region.SmallPatch`, `Region.GasPuddle`,
  `Region.OilPuddle`, `Object.GasCan`, `Object.ExplosiveBarrel`, `Tree.BigJungleTree` in `world1`;
  the three regions in `world2`). JackAll cannot decode these, so the analysis lists each as one
  whole-`Description` change. `world2` leaves its bush, gas can, barrel and big jungle tree as
  they were.

`Burnable.Region.Savannah`, the grass fire, takes the one-byte edit: budget modifier and fear level
`1` -> `0`.

## Uncertain

- Which reading is right: these are real edits, not decoding artifacts. The 45 one-byte documents
  differ from the base game in exactly that byte, and a document holding the string `"0"` twice
  cannot come out of an RML writer, which interns strings - so the game reads `0` wherever the
  decoder does. The intent was clearly `fFireBudgetCostModifier` -> `0` (the Jungle edit and the
  insertions only touch that attribute); every other `1` -> `0` is collateral of the shared string.
- The collateral is not harmless on paper: `fBurnStimRadius` `0` should stop those objects igniting
  their neighbours, and `fFearStimRadius`/`Level` `0` stops mercs fearing them. Not seen in game.
- In the ten inserted documents every string after the insertion is read two bytes early, so those
  effects' later attributes and child elements (`Fire_EmitterParams`, `Fire_BurnStim`,
  `Fire_FearStim`, `BurnSoundParams`, the regions' `decalDecal` and `ResetAfterBurnDelay`) are
  garbled, and their budget modifier still reads the earlier `1`. What the engine does with them
  (defaults, or skipping the effect) is not traced; they include the grass-patch and fuel-puddle
  regions.
- `Burnable.Object.StrawRoof01`'s name hash is mangled in the same file; that is in
  `noise-player-hash-mangling`.
