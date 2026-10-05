---
title: Unchanged archetype copies
kind: noise
claims: []
match:
  - "generated/entitylibrarypatchoverride.fcb/player/movementcurves/*.xml"
  - "generated/entitylibrarypatchoverride.fcb/curves/locomotion/sprint.xml"
  - "generated/entitylibrarypatchoverride.fcb/enemy_archetypes/{corpses,partnermissions}/*.xml"
exclude: []
requires: []
verified: diff
---

# Unchanged archetype copies

Sixteen archetypes copied into `generated/entitylibrarypatchoverride.fcb` as the base game's
`worlds/world1` and `worlds/world2` have them, so shadowing those changes nothing. Compared field by
field against both world libraries:

- `Curves.Locomotion.Sprint` and the nine `player.MovementCurves.*` (`DiveCurve`, `SwimCurve`,
  `Movement_leftright`, `Movement_updown`, their `_PS3` twins and the three `*_SIXAXIS` vehicle
  curves): no difference.
- The six corpses `enemy_archetypes.Corpses.Corpse`, `Corpses.ArmsMerchantCorpse` and
  `PartnerMissions.AmericanCorpse`, `BritishCorpse`, `FrenchCorpse`, `PredecessorCorpse`: only
  `CharacterParams/fMaxSlope` `60.000004` -> `60`, the editor's float rounding. They were copied with
  the soldiers (`player-enemy-override-copies`), but their `bUseRigidBased` is already `False`.
