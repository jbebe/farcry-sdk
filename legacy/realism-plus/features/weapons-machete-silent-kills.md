---
title: Silent machete assassinations
kind: component
bundle: gameplay
status: located
systems: [weapons, ai]
match:
  - "scripts/engine/objects/pawn/statemachine/weapons.gosm.xml#State[15]/**"
  - "graphics/characters/_common/animations/weapons/handtohand/machete/sync_finishground/3rdge_syns_finishground_+000fw_nowep_i1.mab"
exclude: []
requires: []
verified: diff
---

# Silent machete assassinations

A machete attack from behind an unaware enemy kills him at once and without a scream.

## How

- **The kill.** `scripts/engine/objects/pawn/statemachine/weapons.gosm.xml`, state
  `Machete/MeleeAttackBegin` (`State[15]`, the start of a successful sneak attack), gains a first
  event `Opponent plays finish ground anim` (`CGOStateEventPawn`, 15-16% of the animation,
  `requestType` `12`, `simpleEventID` `DoubleMacheteKillOnOpponentEventName`), the event the
  two-handed finishing move sends. The base game's `Send opponent in health failure` at 60%, which
  only knocks the victim down wounded, stays. The `NoControl beautifier` event (`Event[4]`) moves from
  0-1% to 12-17% with `alwaysTrigger` `0` -> `1` and `triggeredOnBegin` `1` -> `0`.
- **The silence.** The mod's `patch.dat` carries
  `graphics/characters/_common/animations/weapons/handtohand/machete/sync_finishground/3rdge_syns_finishground_+000fw_nowep_i1.mab`
  (the base game's is 13,088 bytes) as a byte-identical copy of the base game's
  `.../handtohand/machete/3rdge_uppb_lethalslash_+000fw_hhmac_i1.mab` (7,856 bytes), the step the
  author's guide gives for silent assassinations
  ([weapons](../../../docs/docs/modding/guide/weapons.md#guide---silent-machete-assassinations)).

## Depends on

Nothing in this mod. Scubrah's Patch reaches the same result differently: it renames the existing
health-failure event instead of adding one, and deletes the victim's `DeathBark` in `hmr.gosm.xml`
([`machete-stealth-kills`](../../scubrahs-patch/features/machete-stealth-kills.md)); this mod does
not touch `hmr.gosm.xml` or `bIsSilent`.

## Uncertain

- That the replaced animation is what silences the victim is the guide's claim; the animation's
  sound events are not decoded here.
