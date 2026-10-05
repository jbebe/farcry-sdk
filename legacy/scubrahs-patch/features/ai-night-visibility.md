---
title: Mercs see further at night
kind: component
bundle: balancing
claims:
  - "Slightly improved AI visibility of the player at night"
status: located
systems: [ai]
match: []
exclude: []
requires: [player-enemy-override-copies]
verified: diff
---

# Mercs see further at night

Enemy soldiers' night-time sight is cut less, so the player is spotted a little more readily after
dark.

## How

`CFCXAIComponent/AIObject/CPawnAgent/SensorySystem/FOVParameters/FOVMultipliers/fNightTimeMultiplier`
`0.5` -> `0.6` on the 21 soldier archetypes the mod copies into
`generated/entitylibrarypatchoverride.fcb` (Blue and Red faction riflemen, machine gunners,
shotgunners, rocket men, snipers, mortar men, the Carl Gustaf gunner and the assassination target).

## Depends on

The value exists only in those copies, whole units owned by `player-enemy-override-copies`; this
page has no change of its own.

## Uncertain

- Read as a multiplier on the sight cone or range at night; the engine's use of it is not traced.
