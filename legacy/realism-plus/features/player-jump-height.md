---
title: Higher jump, with fall damage moved to match
kind: component
bundle: gameplay
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/**#**/{fJumpHeight,fJumpHeightExhausted}"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/DefaultCountersService/JumpDamage@*"
exclude: []
requires: []
verified: diff
---

# Higher jump, with fall damage moved to match

The player jumps 40% higher (half as high again when exhausted), and falls have to be a little
faster before they hurt.

## How

- `CPawn/Body/fJumpHeight` `1` -> `1.4` and `fJumpHeightExhausted` `0.4` -> `0.6` on the twelve
  `player.MainCharacter.PawnPlayer.<character>` archetypes, the mod's copies in
  `generated/entitylibrarypatchoverride.fcb` (24 values). The mod's new `Anonymous_Mercenary`
  character carries the same values inside its own whole unit (`player-playable-characters`).
- `gamemodesconfig.xml` single-player `DefaultCountersService/JumpDamage`: `fMinSpeedFallDamage` `14`
  -> `17`, `fMaxSpeedFallDamage` `17` -> `20`, `iMaxFallLevelStim` `32` -> `26`. The guide pairs a
  higher jump with these so landing a jump does not hurt
  ([player character](../../../docs/docs/modding/guide/player-character.md#fall-damage)).

## Depends on

Nothing. Scubrah's Patch raises `fJumpHeight` to `1.1`
([`jump-height`](../../scubrahs-patch/features/jump-height.md)).
