---
title: Player archetype copies
kind: shared
status: located
systems: [player]
match:
  - "generated/entitylibrarypatchoverride.fcb/player/maincharacter/pawnplayer**"
exclude: []
requires: []
verified: diff
---

# Player archetype copies

A shared piece: the mod copies `player.MainCharacter.PawnPlayer` and its twelve per-character
children (`PawnPlayer.Andre_Hyppolite` ... `PawnPlayer.Xianyong_Bai`) into
`generated/entitylibrarypatchoverride.fcb`, which the base game keeps only in the world libraries,
so these copies are the ones the game reads.

## How

Thirteen whole new units, 858 KB each. Against `world1` and `world2`, each changes `CPawn/Body`:

- `fSwimmingMaxSpeed` `5` -> `6`, `fSwimmingAcceleration` `5` -> `6`, `fDivingMaxSpeed` `5` -> `6`,
  `fDivingAcceleration` `5` -> `6` - `swim-speed`
- `fJumpHeight` `1` -> `1.1` - `jump-height`
- `CCharacterPhysComponent/CharacterParams/fMaxSlope` `60.000004` -> `60`, the editor's float
  rounding
