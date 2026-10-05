---
title: Higher jump, with fall damage nudged
kind: component
bundle: gameplay
claims: []
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer{,/*}.xml#**/CPawn/Body/fJumpHeight"
  - "engine/gamemodes/gamemodesconfig.xml#GameModeProperties/DefaultCountersService/JumpDamage@*"
exclude: []
requires: []
verified: diff
---

# Higher jump, with fall damage nudged

The player jumps half as high again, and falls have to be a little faster before they hurt.

## How

- `CPawn/Body/fJumpHeight` `1` -> `1.5` on the 13 player archetypes (`PawnPlayer` and its twelve
  characters), the mod's copies in `generated/entitylibrarypatchoverride.fcb`.
- `gamemodesconfig.xml` single-player `DefaultCountersService/JumpDamage`: `fMinSpeedFallDamage`
  `14` -> `15`, `fMaxSpeedFallDamage` `17` -> `18`, `iMaxFallLevelStim` `32` -> `31`.

## Depends on

Nothing. Realism Plus does the same with `1.4` and a larger fall-damage shift
([`player-jump-height`](../../realism-plus/features/player-jump-height.md),
[player character guide](../../../docs/docs/modding/guide/player-character.md#fall-damage)).

## Uncertain

- Not a line of the readme.
