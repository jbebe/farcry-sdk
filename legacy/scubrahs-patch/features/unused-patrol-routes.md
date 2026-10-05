---
title: Unused patrol routes around Pala enabled
kind: component
bundle: gameplay
claims:
  - "Enabled unused patrol vehicle routes in the surrounding areas of Pala"
status: located
systems: [patrols, world]
match:
  # the four new patrol groups, ids 2056425657155411111 (Arena) ...422222 (LumberYard),
  # ...433333 (Slaughterhouse), ...444444 (FishingCamp)
  - "_hash/{6a78eb66,6e5e5d90,a21a9129,e5c4b8ec}.lua"
  - "worlds/world1/generated/world1.omnis.fcb/dominoomnientity_patrols_20564256571554{11111,22222,33333,44444}.*"
  - "worlds/world1/generated/world1.game.xml/missions/ghostpatrols/20564256571554{11111,22222,33333,44444}/**"
  - 'worlds/world1/generated/world1.mapsdata.fcb/_layout.xml#layer[missions\ghostpatrols\20564256571554{11111,22222,33333,44444}\*]'
  - "worlds/world1/generated/world1.mapsdata.fcb/patrols.rover_3_{arena,fishing,lumber,slaughter}.**"
  # the reshaped paths they drive
  - "worlds/world1/generated/world1.mapsdata.fcb/vehiclepatrolpath_*#hidShapePoints/**"
exclude: []
requires: [randomized-patrols-core]
verified: diff
---

# Unused patrol routes around Pala enabled

Four vehicle patrol paths around Pala that the base game ships but never puts a vehicle on - by the
arena, the lumber yard, the slaughterhouse and the fishing camp - get randomized patrols.

## How

- Four new patrol groups in `world1`, built exactly like the ones of `randomized-patrols-core` (17
  `GhostPatrols` missions each, one entity per mission, one script and one
  `DominoOmniEntity_Patrols_<id>` each). Their ids are hand-made (`2056425657155411111`,
  `...422222`, `...433333`, `...444444`), and so are their 68 entities, `Patrols.Rover_3_Arena`,
  `_Lumber`, `_Slaughter` and `_Fishing` (17 each), whose `entPathToFollow` is
  `VehiclePatrolPath_Arena`, `VehiclePatrolPath_LumberYard`, `vehiclePatrolPath_Slaughterhouse` and
  `VehiclePatrolPath_FishingCamp`.
- Their four scripts differ from the other 59 only in the first delay: `random(600, 900)` s instead
  of `random(1, 300)`, commented "activate these patrols 10-15 minutes after new game start" -
  inferred to keep them off the road during the opening in Pala.
- Three of the paths are reshaped in `world1.mapsdata.fcb` (`hidShapePoints`): `FishingCamp` 85 -> 75
  points (10 removed, 7 moved), `LumberYard` 106 -> 93 (13 removed, a loop at its end), and
  `Slaughterhouse` 86 -> 83 (3 removed, 30 moved). `VehiclePatrolPath_Arena` is used unchanged.

## Depends on

`randomized-patrols-core` (the archetypes these slots spawn, and through it `faction-conflicts` and
the `"Patrol"` delay preset).

## Uncertain

- That the paths were edited so vehicles can actually drive them (the removed points sit on
  their ends and corners) is an inference; nothing states why.
